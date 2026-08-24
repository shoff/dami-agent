using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Approvals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Approvals;

/// <summary>Approvals over PostgreSQL.</summary>
/// <remarks>
/// Resolution is guarded in SQL: only a Pending row can change, so two answers to the
/// same request cannot both win and an approval cannot be un-denied.
/// </remarks>
public sealed class PostgresApprovalService : IApprovalService
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresApprovalService> logger;

    /// <summary>Creates the service.</summary>
    public PostgresApprovalService(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresApprovalService> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.approvals";

    /// <inheritdoc />
    public async Task RequestAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var command = this.dataSource.CreateCommand(
            ApprovalRequestCommand.InsertSql(this.Table));
        ApprovalRequestCommand.AddParameters(command, request);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ApprovalRequest> PendingAsync(CancellationToken cancellationToken)
    {
        var command = this.dataSource.CreateCommand(
            $"{SelectList(this.Table)} where status = 'Pending' order by requested_at;");
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ResolveAsync(
        Guid approvalId,
        ApprovalStatus resolution,
        string? note,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        if (resolution == ApprovalStatus.Pending)
        {
            throw new ArgumentException("Pending is not a resolution.", nameof(resolution));
        }

        await using var command = this.dataSource.CreateCommand(
            $"""
            update {this.Table}
               set status = @status, resolved_at = @at, resolved_note = @note
             where approval_id = @id and status = 'Pending';
            """);
        command.Parameters.AddWithValue("id", approvalId);
        command.Parameters.AddWithValue("status", resolution.ToString());
        command.Parameters.AddWithValue("at", resolvedAt);
        command.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);

        var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Approval {ApprovalId} resolved {Resolution}: {Changed} row(s)", approvalId, resolution, changed);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<ApprovalRequest?> FindAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"{SelectList(this.Table)} where approval_id = @id;");
        command.Parameters.AddWithValue("id", approvalId);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static string SelectList(string table)
    {
        return $"""
            select approval_id, trace_id, requested_by, action, scope, resource, status,
                   requested_at, resolved_at, resolved_note, expires_at, origin, parent_span_id
            from {table}
            """;
    }

    private static async IAsyncEnumerable<ApprovalRequest> StreamAsync(
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

    private static ApprovalRequest Read(NpgsqlDataReader reader)
    {
        return new ApprovalRequest(
            approvalId: reader.GetGuid(0),
            traceId: reader.GetGuid(1),
            requestedBy: reader.GetString(2),
            action: reader.GetString(3),
            scope: reader.GetString(4),
            resource: reader.GetString(5),
            status: Enum.Parse<ApprovalStatus>(reader.GetString(6)),
            requestedAt: reader.GetFieldValue<DateTimeOffset>(7),
            resolvedAt: reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            resolvedNote: reader.IsDBNull(9) ? null : reader.GetString(9),
            expiresAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            origin: Enum.Parse<Dami.Contracts.Events.ExecutionOrigin>(reader.GetString(11)),
            parentSpanId: reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }
}
