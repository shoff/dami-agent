using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Memory;

/// <summary>The conclusions ledger over PostgreSQL.</summary>
/// <remarks>
/// Supersession is one transaction rather than two calls, because charter §9.4 requires
/// a correction to replace rather than coexist, and a partial failure that recorded the
/// replacement without retracting the original would leave both active — the exact state
/// the rule forbids.
/// </remarks>
public sealed class PostgresConclusionLedger : IConclusionLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresConclusionLedger> logger;

    /// <summary>Creates the ledger.</summary>
    public PostgresConclusionLedger(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresConclusionLedger> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Schema => this.storeOptions.SchemaName;

    /// <summary>Insert SQL for a conclusion.</summary>
    public static string BuildRecordSql(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return $"""
            insert into {schema}.conclusions
                (conclusion_id, supersedes_id, subject, statement, confidence, source,
                 concluded_at, retracted_at, retraction_reason)
            values
                (@conclusion_id, @supersedes_id, @subject, @statement, @confidence, @source,
                 @concluded_at, @retracted_at, @retraction_reason);
            """;
    }

    /// <summary>Retraction SQL. Only ever sets a retraction; never clears one.</summary>
    public static string BuildRetractSql(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return $"""
            update {schema}.conclusions
               set retracted_at = @retracted_at, retraction_reason = @reason
             where conclusion_id = @conclusion_id and retracted_at is null;
            """;
    }

    /// <summary>Active-set SQL for one subject.</summary>
    public static string BuildActiveSql(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return $"{SelectList(schema)} where c.subject = @subject and c.retracted_at is null order by c.concluded_at desc;";
    }

    /// <inheritdoc />
    public async Task RecordAsync(Conclusion conclusion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conclusion);

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await InsertAsync(connection, transaction, this.Schema, conclusion, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SupersedeAsync(Conclusion replacement, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(reason);

        if (replacement.SupersedesId is not { } supersededId)
        {
            throw new ArgumentException(
                "A replacement must name the conclusion it supersedes.", nameof(replacement));
        }

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await InsertAsync(connection, transaction, this.Schema, replacement, cancellationToken).ConfigureAwait(false);

        // The original stopped being believed the moment the replacement was concluded.
        await RetractAsync(connection, transaction, this.Schema, supersededId, reason, replacement.ConcludedAt, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation(
            "Conclusion {Replacement} superseded {Superseded}: {Reason}", replacement.ConclusionId, supersededId, reason);
    }

    /// <inheritdoc />
    public async Task RetractAsync(
        Guid conclusionId,
        string reason,
        DateTimeOffset retractedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await RetractAsync(connection, null, this.Schema, conclusionId, reason, retractedAt, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Conclusion> ActiveForSubjectAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        await using var command = this.dataSource.CreateCommand(BuildActiveSql(this.Schema));
        command.Parameters.AddWithValue("subject", subject);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return Read(reader, ReadSupporting(reader));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Conclusion> ActiveAsOfAsync(
        DateTimeOffset asOf,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            {SelectList(this.Schema)}
            where c.concluded_at <= @as_of
              and (c.retracted_at is null or c.retracted_at > @as_of)
            order by c.concluded_at desc;
            """);
        command.Parameters.AddWithValue("as_of", asOf);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return Read(reader, ReadSupporting(reader));
        }
    }

    /// <inheritdoc />
    public async Task<Conclusion?> FindAsync(Guid conclusionId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"{SelectList(this.Schema)} where c.conclusion_id = @conclusion_id;");
        command.Parameters.AddWithValue("conclusion_id", conclusionId);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Read(reader, ReadSupporting(reader));
    }

    private static string SelectList(string schema)
    {
        // Provenance rides along via array_agg so set reads stay one query. The active
        // set is designed to be a few hundred rows at most (D-009), so this is cheap.
        return $"""
            select c.conclusion_id, c.supersedes_id, c.subject, c.statement, c.confidence, c.source,
                   c.concluded_at, c.retracted_at, c.retraction_reason,
                   coalesce(
                       (select array_agg(o.observation_id)
                          from {schema}.conclusion_observations o
                         where o.conclusion_id = c.conclusion_id),
                       array[]::uuid[]) as supporting
            from {schema}.conclusions c
            """;
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schema,
        Conclusion conclusion,
        CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(BuildRecordSql(schema), connection, transaction))
        {
            AddConclusionParameters(command, conclusion);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var observationId in conclusion.SupportingObservations)
        {
            await using var link = new NpgsqlCommand(
                $"insert into {schema}.conclusion_observations values (@conclusion_id, @observation_id) "
                + "on conflict do nothing;", connection, transaction);
            link.Parameters.AddWithValue("conclusion_id", conclusion.ConclusionId);
            link.Parameters.AddWithValue("observation_id", observationId);
            await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RetractAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        Guid conclusionId,
        string reason,
        DateTimeOffset retractedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BuildRetractSql(schema), connection, transaction);
        command.Parameters.AddWithValue("conclusion_id", conclusionId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("retracted_at", retractedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddConclusionParameters(NpgsqlCommand command, Conclusion source)
    {
        command.Parameters.AddWithValue("conclusion_id", source.ConclusionId);
        command.Parameters.AddWithValue("supersedes_id", (object?)source.SupersedesId ?? DBNull.Value);
        command.Parameters.AddWithValue("subject", source.Subject);
        command.Parameters.AddWithValue("statement", source.Statement);
        command.Parameters.AddWithValue("confidence", source.Confidence);
        command.Parameters.AddWithValue("source", source.Source.ToString());
        command.Parameters.AddWithValue("concluded_at", source.ConcludedAt);
        command.Parameters.AddWithValue("retracted_at", (object?)source.RetractedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("retraction_reason", (object?)source.RetractionReason ?? DBNull.Value);
    }

    private static IReadOnlyList<Guid> ReadSupporting(NpgsqlDataReader reader)
    {
        return reader.IsDBNull(9) ? [] : reader.GetFieldValue<Guid[]>(9);
    }

    private static Conclusion Read(NpgsqlDataReader reader, IReadOnlyList<Guid> supporting)
    {
        return new Conclusion(
            conclusionId: reader.GetGuid(0),
            supersedesId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
            subject: reader.GetString(2),
            statement: reader.GetString(3),
            confidence: reader.GetDouble(4),
            source: Enum.Parse<ConclusionSource>(reader.GetString(5)),
            concludedAt: reader.GetFieldValue<DateTimeOffset>(6),
            supportingObservations: supporting,
            retractedAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            retractionReason: reader.IsDBNull(8) ? null : reader.GetString(8));
    }

}
