using System.Text.Json;
using Dami.Contracts.Events;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.Events;

/// <summary>Shared PostgreSQL append command for standalone and transactional event writes.</summary>
internal static class ExecutionEventCommand
{
    public static async Task<long> AppendExactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        ExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        long sequence = await AppendAsync(
            connection, transaction, table, executionEvent, cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(ExactMatchSql(table), connection, transaction);
        AddParameters(command, executionEvent);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is not true)
        {
            throw new InvalidOperationException(
                $"Event '{executionEvent.EventId}' already exists with different data.");
        }

        return sequence;
    }

    public static async Task<long> AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        ExecutionEvent executionEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(executionEvent);
        await using var command = new NpgsqlCommand(AppendSql(table), connection, transaction);
        AddParameters(command, executionEvent);
        var sequence = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return sequence is long value
            ? value
            : throw new InvalidOperationException(
                $"Append of event {executionEvent.EventId} returned no sequence.");
    }

    public static string AppendSql(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return $"""
            with appended as (
                insert into {table}
                    (event_id, trace_id, span_id, parent_span_id, origin, actor_id,
                     type, status, occurred_at, label, payload_reference, metadata)
                values
                    (@event_id, @trace_id, @span_id, @parent_span_id, @origin, @actor_id,
                     @type, @status, @occurred_at, @label, @payload_reference, @metadata)
                on conflict (event_id) do nothing
                returning sequence
            )
            select sequence from appended
            union all
            select sequence from {table} where event_id = @event_id
            limit 1;
            """;
    }

    public static void AddParameters(NpgsqlCommand command, ExecutionEvent source)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(source);
        command.Parameters.AddWithValue("event_id", source.EventId);
        command.Parameters.AddWithValue("trace_id", source.TraceId);
        command.Parameters.AddWithValue("span_id", source.SpanId);
        command.Parameters.AddWithValue("parent_span_id", (object?)source.ParentSpanId ?? DBNull.Value);
        command.Parameters.AddWithValue("origin", source.Origin.ToString());
        command.Parameters.AddWithValue("actor_id", source.ActorId);
        command.Parameters.AddWithValue("type", source.Type.ToString());
        command.Parameters.AddWithValue("status", source.Status.ToString());
        command.Parameters.AddWithValue("occurred_at", source.OccurredAt);
        command.Parameters.AddWithValue("label", source.Label);
        command.Parameters.AddWithValue("payload_reference", (object?)source.PayloadReference ?? DBNull.Value);

        var metadata = source.Metadata is null
            ? (object)DBNull.Value
            : JsonSerializer.Serialize(source.Metadata);
        command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb) { Value = metadata });
    }

    private static string ExactMatchSql(string table)
    {
        return $"""
            select exists (
                select 1 from {table}
                 where event_id = @event_id
                   and trace_id = @trace_id
                   and span_id = @span_id
                   and parent_span_id is not distinct from @parent_span_id
                   and origin = @origin
                   and actor_id = @actor_id
                   and type = @type
                   and status = @status
                   and occurred_at = @occurred_at
                   and label = @label
                   and payload_reference is not distinct from @payload_reference
                   and metadata is not distinct from @metadata);
            """;
    }
}
