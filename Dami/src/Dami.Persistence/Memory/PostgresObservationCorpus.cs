using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.Memory;

/// <summary>The observation corpus over PostgreSQL.</summary>
/// <remarks>
/// Append and read only. <c>on conflict do nothing</c> is the load-bearing clause: a
/// retrying collector re-sends what it already sent, and discarding the repeat is what
/// keeps "never edited" true in the presence of at-least-once delivery.
/// </remarks>
public sealed class PostgresObservationCorpus : IObservationCorpus
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresObservationCorpus> logger;

    /// <summary>Creates the corpus.</summary>
    public PostgresObservationCorpus(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresObservationCorpus> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.observations";

    /// <summary>Insert SQL. Discards a repeat rather than applying it.</summary>
    public static string BuildRecordSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return $"""
            insert into {table} (observation_id, occurred_at, source, body, metadata)
            values (@observation_id, @occurred_at, @source, @body, @metadata)
            on conflict (observation_id) do nothing;
            """;
    }

    /// <summary>Window SQL. Half-open, so adjacent windows neither overlap nor gap.</summary>
    public static string BuildBetweenSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"{SelectList(table)} where occurred_at >= @from and occurred_at < @to order by occurred_at;";
    }

    /// <summary>Source SQL, newest first.</summary>
    public static string BuildFromSourceSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"{SelectList(table)} where source = @source order by occurred_at desc limit @limit;";
    }

    /// <inheritdoc />
    public async Task RecordAsync(Observation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        await using var command = this.dataSource.CreateCommand(BuildRecordSql(this.Table));
        command.Parameters.AddWithValue("observation_id", observation.ObservationId);
        command.Parameters.AddWithValue("occurred_at", observation.OccurredAt);
        command.Parameters.AddWithValue("source", observation.Source);
        command.Parameters.AddWithValue("body", observation.Body);

        var metadata = observation.Metadata is null
            ? (object)DBNull.Value
            : JsonSerializer.Serialize(observation.Metadata);
        command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb) { Value = metadata });

        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (written == 0)
        {
            this.logger.LogDebug(
                "Observation {ObservationId} was already recorded; the repeat was discarded",
                observation.ObservationId);
        }
    }

    /// <inheritdoc />
    public async Task<Observation?> FindAsync(Guid observationId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"{SelectList(this.Table)} where observation_id = @observation_id;");
        command.Parameters.AddWithValue("observation_id", observationId);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Observation> BetweenAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var command = this.dataSource.CreateCommand(BuildBetweenSql(this.Table));
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Observation> FromSourceAsync(
        string source,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(BuildFromSourceSql(this.Table));
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    private static string SelectList(string table)
    {
        // B10: occurred_at reads through the repair sidecar. The observations table is
        // append-only, so recovered dates live in observation_date_repairs and every
        // read — including the range filters layered on top — sees the repaired value.
        var schema = table[..table.IndexOf('.', StringComparison.Ordinal)];
        return $"""
            select observation_id, occurred_at, recorded_at, source, body, metadata
            from (select o.observation_id,
                         coalesce(r.repaired_occurred_at, o.occurred_at) as occurred_at,
                         o.recorded_at, o.source,
                         coalesce(c.curated_body, o.body) as body,
                         o.metadata
                    from {table} o
                    left join {schema}.observation_date_repairs r using (observation_id)
                    left join {schema}.observation_curations c using (observation_id)) repaired
            """;
    }

    private static async IAsyncEnumerable<Observation> StreamAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return Read(reader);
            }
        }
    }

    private static Observation Read(NpgsqlDataReader reader)
    {
        var metadata = reader.IsDBNull(5)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5));

        return new Observation(
            observationId: reader.GetGuid(0),
            occurredAt: reader.GetFieldValue<DateTimeOffset>(1),
            source: reader.GetString(3),
            body: reader.GetString(4),
            metadata: metadata,
            recordedAt: reader.GetFieldValue<DateTimeOffset>(2));
    }
}
