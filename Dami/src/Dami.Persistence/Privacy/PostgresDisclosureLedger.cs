using Dami.Contracts.Privacy;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Privacy;

/// <summary>PostgreSQL record of gate decisions and Steve's corrections (migration 032).</summary>
public sealed class PostgresDisclosureLedger : IDisclosureLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the ledger.</summary>
    public PostgresDisclosureLedger(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        Guid traceId,
        string question,
        IReadOnlyList<DisclosedItem> decisions,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(decisions);
        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var decision in decisions)
        {
            await using var command = new NpgsqlCommand(
                $"""
                insert into {this.schema}.disclosure_decisions
                    (decision_id, trace_id, question, original, disclosure, sendable, reason, decided_at)
                values (@id, @trace, @question, @original, @disclosure, @sendable, @reason, @at);
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("trace", traceId);
            command.Parameters.AddWithValue("question", question);
            command.Parameters.AddWithValue("original", decision.Original);
            command.Parameters.AddWithValue("disclosure", decision.Disclosure.ToString());
            command.Parameters.AddWithValue("sendable", decision.Sendable);
            command.Parameters.AddWithValue("reason", decision.Reason);
            command.Parameters.AddWithValue("at", decidedAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DisclosureDecision>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        return this.ReadAsync(limit, correctedOnly: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DisclosureDecision>> CorrectionsAsync(int limit, CancellationToken cancellationToken)
    {
        return this.ReadAsync(limit, correctedOnly: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CorrectAsync(
        Guid decisionId,
        DisclosureCorrection correction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correction);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.schema}.disclosure_corrections
                (decision_id, corrected, note, corrected_by, corrected_at)
            select @id, @corrected, @note, @by, @at
              from {this.schema}.disclosure_decisions
             where decision_id = @id
            on conflict (decision_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", decisionId);
        command.Parameters.AddWithValue("corrected", correction.Corrected.ToString());
        command.Parameters.AddWithValue("note", correction.Note);
        command.Parameters.AddWithValue("by", correction.CorrectedBy);
        command.Parameters.AddWithValue("at", correction.CorrectedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task<IReadOnlyList<DisclosureDecision>> ReadAsync(
        int limit,
        bool correctedOnly,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var command = this.dataSource.CreateCommand(
            $"""
            select d.decision_id, d.trace_id, d.question, d.original, d.disclosure, d.sendable, d.reason,
                   d.decided_at, c.corrected, c.note, c.corrected_by, c.corrected_at
              from {this.schema}.disclosure_decisions d
              left join {this.schema}.disclosure_corrections c on c.decision_id = d.decision_id
             where (not @correctedOnly) or c.decision_id is not null
             order by coalesce(c.corrected_at, d.decided_at) desc, d.decision_id
             limit @limit;
            """);
        command.Parameters.AddWithValue("correctedOnly", correctedOnly);
        command.Parameters.AddWithValue("limit", limit);
        var decisions = new List<DisclosureDecision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            decisions.Add(Read(reader));
        }

        return decisions;
    }

    private static DisclosureDecision Read(NpgsqlDataReader reader)
    {
        var correction = reader.IsDBNull(8)
            ? null
            : new DisclosureCorrection(
                Enum.Parse<Disclosure>(reader.GetString(8)), reader.GetString(9), reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11));
        return new DisclosureDecision(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            Enum.Parse<Disclosure>(reader.GetString(4)), reader.GetString(5), reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7), correction);
    }
}
