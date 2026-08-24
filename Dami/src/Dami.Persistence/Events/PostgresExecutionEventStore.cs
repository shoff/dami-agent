using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dami.Contracts.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Events;

/// <summary>The canonical execution event store (D-017), backed by PostgreSQL.</summary>
/// <remarks>
/// Append and read only. The absence of update and delete is not politeness: the
/// runtime role holds no such privilege and a database trigger refuses both even for the
/// owner, so a defect here cannot rewrite history.
/// </remarks>
public sealed class PostgresExecutionEventStore : IExecutionEventStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresExecutionEventStore> logger;

    /// <summary>Creates the store.</summary>
    public PostgresExecutionEventStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresExecutionEventStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.execution_events";

    /// <summary>Append SQL. Pure builder so the projection is testable without a database.</summary>
    /// <remarks>
    /// <c>on conflict do nothing</c> plus the <c>select</c> fallback makes the append
    /// idempotent on <c>event_id</c>: a retry after an ambiguous failure returns the
    /// sequence already stored instead of duplicating the event or throwing.
    /// </remarks>
    public static string BuildAppendSql(string table)
    {
        return ExecutionEventCommand.AppendSql(table);
    }

    /// <summary>Replay SQL, ordered by the sequence the store assigned.</summary>
    public static string BuildReplaySql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"{BuildSelectList(table)} where trace_id = @trace_id order by sequence;";
    }

    /// <summary>Catch-up SQL for a reconnecting client.</summary>
    public static string BuildReadSinceSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"{BuildSelectList(table)} where sequence > @after order by sequence limit @limit;";
    }

    /// <inheritdoc />
    public async Task<long> AppendAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);

        await using var command = this.dataSource.CreateCommand(BuildAppendSql(this.Table));
        ExecutionEventCommand.AddParameters(command, executionEvent);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (scalar is not long sequence)
        {
            throw new InvalidOperationException(
                $"Append of event {executionEvent.EventId} returned no sequence.");
        }

        this.logger.LogDebug(
            "Appended {Type} for trace {TraceId} at sequence {Sequence}",
            executionEvent.Type,
            executionEvent.TraceId,
            sequence);

        return sequence;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ExecutionEvent> ReplayAsync(Guid traceId, CancellationToken cancellationToken)
    {
        var command = this.dataSource.CreateCommand(BuildReplaySql(this.Table));
        command.Parameters.AddWithValue("trace_id", traceId);
        return this.StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> FindTraceByPrefixAsync(string hexPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hexPrefix);

        await using var command = this.dataSource.CreateCommand(
            $"""
            select distinct trace_id from {this.Table}
             where replace(trace_id::text, '-', '') like @prefix || '%'
             limit 2;
            """);
        command.Parameters.AddWithValue("prefix", hexPrefix.ToLowerInvariant());
        var matches = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(reader.GetGuid(0));
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ExecutionEvent> ReadSinceAsync(
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(BuildReadSinceSql(this.Table));
        command.Parameters.AddWithValue("after", afterSequence);
        command.Parameters.AddWithValue("limit", limit);
        return this.StreamAsync(command, cancellationToken);
    }

    private static string BuildSelectList(string table)
    {
        return $"""
            select sequence, event_id, trace_id, span_id, parent_span_id, origin, actor_id,
                   type, status, occurred_at, label, payload_reference, metadata
            from {table}
            """;
    }

    private async IAsyncEnumerable<ExecutionEvent> StreamAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return Read(reader);
            }
        }
    }

    private static ExecutionEvent Read(NpgsqlDataReader reader)
    {
        var metadata = reader.IsDBNull(12)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(12));

        return new ExecutionEvent(
            eventId: reader.GetGuid(1),
            traceId: reader.GetGuid(2),
            spanId: reader.GetGuid(3),
            parentSpanId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
            origin: Enum.Parse<ExecutionOrigin>(reader.GetString(5)),
            actorId: reader.GetString(6),
            type: Enum.Parse<ExecutionEventType>(reader.GetString(7)),
            status: Enum.Parse<ExecutionStatus>(reader.GetString(8)),
            occurredAt: reader.GetFieldValue<DateTimeOffset>(9),
            label: reader.GetString(10),
            payloadReference: reader.IsDBNull(11) ? null : reader.GetString(11),
            metadata: metadata,
            sequence: reader.GetInt64(0));
    }
}
