using Dami.Contracts.Approvals;
using Npgsql;

namespace Dami.Persistence.Approvals;

/// <summary>Shared SQL and parameters for idempotently filing an approval request.</summary>
internal static class ApprovalRequestCommand
{
    public static string InsertSql(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return $"""
            insert into {table}
                (approval_id, trace_id, requested_by, action, scope, resource, status,
                 requested_at, expires_at)
            values (@id, @trace, @by, @action, @scope, @resource, @status, @at, @expires)
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
    }
}
