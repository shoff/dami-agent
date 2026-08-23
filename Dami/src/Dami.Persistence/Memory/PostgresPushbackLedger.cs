using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Memory;

/// <summary>The pushback ledger over PostgreSQL.</summary>
/// <remarks>
/// D-011's instrument. <see cref="RateAsync"/> is the point of the whole table: a total
/// that falls across successive quarters is direct evidence the tuning loop is eating the
/// auditor, and that drift is invisible as tone.
/// </remarks>
public sealed class PostgresPushbackLedger : IPushbackLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresPushbackLedger> logger;

    /// <summary>Creates the ledger.</summary>
    public PostgresPushbackLedger(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresPushbackLedger> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.pushbacks";

    /// <summary>Insert SQL.</summary>
    public static string BuildRecordSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return $"""
            insert into {table}
                (pushback_id, trace_id, challenge, challenged_assumption, outcome,
                 occurred_at, follow_up_note)
            values
                (@pushback_id, @trace_id, @challenge, @challenged_assumption, @outcome,
                 @occurred_at, @follow_up_note)
            on conflict (pushback_id) do nothing;
            """;
    }

    /// <summary>Outcome-update SQL.</summary>
    public static string BuildResolveSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"update {table} set outcome = @outcome, follow_up_note = @note where pushback_id = @id;";
    }

    /// <summary>Window count SQL, grouped by outcome.</summary>
    /// <remarks>
    /// The window is half-open, <c>[from, to)</c>, so consecutive quarters neither
    /// double-count a challenge on a boundary nor lose one.
    /// </remarks>
    public static string BuildRateSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return $"""
            select outcome, count(*)
              from {table}
             where occurred_at >= @from and occurred_at < @to
             group by outcome;
            """;
    }

    /// <inheritdoc />
    public async Task RecordAsync(PushbackRecord pushback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pushback);

        await using var command = this.dataSource.CreateCommand(BuildRecordSql(this.Table));
        command.Parameters.AddWithValue("pushback_id", pushback.PushbackId);
        command.Parameters.AddWithValue("trace_id", pushback.TraceId);
        command.Parameters.AddWithValue("challenge", pushback.Challenge);
        command.Parameters.AddWithValue("challenged_assumption", pushback.ChallengedAssumption);
        command.Parameters.AddWithValue("outcome", pushback.Outcome.ToString());
        command.Parameters.AddWithValue("occurred_at", pushback.OccurredAt);
        command.Parameters.AddWithValue("follow_up_note", (object?)pushback.FollowUpNote ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResolveAsync(
        Guid pushbackId,
        PushbackOutcome outcome,
        string? followUpNote,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(BuildResolveSql(this.Table));
        command.Parameters.AddWithValue("id", pushbackId);
        command.Parameters.AddWithValue("outcome", outcome.ToString());
        command.Parameters.AddWithValue("note", (object?)followUpNote ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation("Pushback {PushbackId} resolved as {Outcome}", pushbackId, outcome);
    }

    /// <inheritdoc />
    public async Task<PushbackRate> RateAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<PushbackOutcome, int>();

        await using (var command = this.dataSource.CreateCommand(BuildRateSql(this.Table)))
        {
            command.Parameters.AddWithValue("from", from);
            command.Parameters.AddWithValue("to", to);

            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                counts[Enum.Parse<PushbackOutcome>(reader.GetString(0))] = (int)reader.GetInt64(1);
            }
        }

        return new PushbackRate(
            from,
            to,
            counts.Values.Sum(),
            Count(counts, PushbackOutcome.Accepted),
            Count(counts, PushbackOutcome.Rejected),
            Count(counts, PushbackOutcome.Deferred),
            Count(counts, PushbackOutcome.Unresolved));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PushbackRecord> BetweenAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select pushback_id, trace_id, challenge, challenged_assumption, outcome,
                   occurred_at, follow_up_note
              from {this.Table}
             where occurred_at >= @from and occurred_at < @to
             order by occurred_at;
            """);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return Read(reader);
        }
    }

    private static int Count(IReadOnlyDictionary<PushbackOutcome, int> counts, PushbackOutcome outcome)
    {
        return counts.TryGetValue(outcome, out var found) ? found : 0;
    }

    private static PushbackRecord Read(NpgsqlDataReader reader)
    {
        return new PushbackRecord(
            pushbackId: reader.GetGuid(0),
            traceId: reader.GetGuid(1),
            challenge: reader.GetString(2),
            challengedAssumption: reader.GetString(3),
            outcome: Enum.Parse<PushbackOutcome>(reader.GetString(4)),
            occurredAt: reader.GetFieldValue<DateTimeOffset>(5),
            followUpNote: reader.IsDBNull(6) ? null : reader.GetString(6));
    }
}
