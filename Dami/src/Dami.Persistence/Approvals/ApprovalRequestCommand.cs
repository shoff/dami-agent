using Dami.Contracts.Approvals;
using Npgsql;

namespace Dami.Persistence.Approvals;

/// <summary>Shared SQL and parameters for idempotently filing an approval request.</summary>
internal static class ApprovalRequestCommand
{
    public static async Task EnsureExactReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ExactReplaySql(table), connection, transaction);
        AddParameters(command, request);
        var exact = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (exact is not true)
        {
            throw new InvalidOperationException(
                $"Approval '{request.ApprovalId}' conflicts with its immutable stored value.");
        }
    }

    public static string InsertSql(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return $"""
            insert into {table}
                (approval_id, trace_id, requested_by, action, scope, resource, status,
                 requested_at, expires_at, origin, parent_span_id)
            values (@id, @trace, @by, @action, @scope, @resource, @status, @at, @expires,
                    @origin, @parent_span)
            on conflict (approval_id) do nothing;
            """;
    }

    public static void AddParameters(NpgsqlCommand command, ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(request);
        command.Parameters.AddWithValue("id", request.ApprovalId);
        command.Parameters.AddWithValue("trace", request.TraceId);
        command.Parameters.AddWithValue("by", request.RequestedBy);
        command.Parameters.AddWithValue("action", request.Action);
        command.Parameters.AddWithValue("scope", request.Scope);
        command.Parameters.AddWithValue("resource", request.Resource);
        command.Parameters.AddWithValue("status", request.Status.ToString());
        command.Parameters.AddWithValue("at", request.RequestedAt);
        command.Parameters.AddWithValue("expires", (object?)request.ExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("origin", request.Origin.ToString());
        command.Parameters.AddWithValue(
            "parent_span", (object?)request.ParentSpanId ?? DBNull.Value);
    }

    private static string ExactReplaySql(string table)
    {
        return $"""
            select trace_id = @trace
               and requested_by = @by
               and action = @action
               and scope = @scope
               and resource = @resource
               and status = @status
               and requested_at = @at
               and origin = @origin
               and parent_span_id is not distinct from @parent_span
               and resolved_at is null
               and resolved_note is null
               and expires_at is not distinct from @expires
              from {table}
             where approval_id = @id;
            """;
    }
}
