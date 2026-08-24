using Dami.Contracts.Approvals;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Approvals;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.ToolStaging;

/// <summary>Exact-version promotion requests paired transactionally with approvals.</summary>
public sealed class PostgresToolPromotionStore : IToolPromotionStore
{
    private readonly string approvalsTable;
    private readonly NpgsqlDataSource dataSource;
    private readonly string eventsTable;
    private readonly string table;

    /// <summary>Creates the PostgreSQL promotion store.</summary>
    public PostgresToolPromotionStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        string schema = options.Value.SchemaName;
        this.approvalsTable = $"{schema}.approvals";
        this.eventsTable = $"{schema}.execution_events";
        this.table = $"{schema}.tool_promotions";
    }

    /// <inheritdoc />
    public async Task<ToolPromotionRequest> RequestAsync(
        ToolPromotionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await this.InsertApprovalAsync(
            connection, transaction, request.Approval, cancellationToken).ConfigureAwait(false);
        await this.InsertPromotionAsync(
            connection, transaction, request, cancellationToken).ConfigureAwait(false);
        ToolPromotionRequest accepted = await this.FindRequiredAsync(
            connection, transaction, request.PromotionId, cancellationToken).ConfigureAwait(false);
        EnsureExactRetry(request, accepted);
        await this.AppendEventsAsync(
            connection, transaction, accepted, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    /// <inheritdoc />
    public async Task<ToolPromotionRequest?> FindByApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException("An approval identifier cannot be empty.", nameof(approvalId));
        }

        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindCoreAsync(
            connection, null, "p.approval_id", approvalId, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            ApprovalRequestCommand.InsertSql(this.approvalsTable), connection, transaction);
        ApprovalRequestCommand.AddParameters(command, approval);
        int inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
        {
            await ApprovalRequestCommand.EnsureExactReplayAsync(
                connection, transaction, this.approvalsTable, approval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task InsertPromotionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolPromotionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            insert into {this.table}
                (promotion_id, approval_id, proposal_id, artifact_version)
            values (@promotion, @approval, @proposal, @version)
            on conflict (promotion_id) do nothing;
            """, connection, transaction);
        AddParameters(command, request);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolPromotionRequest> FindRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        return await this.FindCoreAsync(
            connection, transaction, "p.promotion_id", promotionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tool promotion could not be reloaded.");
    }

    private async Task<ToolPromotionRequest?> FindCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string keyColumn,
        Guid key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select p.promotion_id, p.proposal_id, p.artifact_version,
                   a.approval_id, a.trace_id, a.requested_by, a.action, a.scope,
                   a.resource, a.requested_at, a.expires_at, a.origin, a.parent_span_id
              from {this.table} p
              join {this.approvalsTable} a on a.approval_id = p.approval_id
             where {keyColumn} = @key;
            """, connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task AppendEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolPromotionRequest request,
        CancellationToken cancellationToken)
    {
        await ExecutionEventCommand.AppendExactAsync(
            connection, transaction, this.eventsTable,
            ApprovalExecutionEventFactory.Requested(request.Approval), cancellationToken)
            .ConfigureAwait(false);
        await ExecutionEventCommand.AppendExactAsync(
            connection, transaction, this.eventsTable,
            ToolPromotionEventFactory.Requested(request), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ToolPromotionRequest> ReadAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        bool expiresNull = await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false);
        bool parentNull = await reader.IsDBNullAsync(12, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? expiresAt = expiresNull
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(10, cancellationToken)
                .ConfigureAwait(false);
        var approval = new ApprovalRequest(
            reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8),
            await reader.GetFieldValueAsync<DateTimeOffset>(9, cancellationToken)
                .ConfigureAwait(false),
            expiresAt: expiresAt,
            origin: Enum.Parse<ExecutionOrigin>(reader.GetString(11)),
            parentSpanId: parentNull ? null : reader.GetGuid(12));
        return new ToolPromotionRequest(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), approval);
    }

    private static void AddParameters(NpgsqlCommand command, ToolPromotionRequest request)
    {
        command.Parameters.AddWithValue("promotion", request.PromotionId);
        command.Parameters.AddWithValue("approval", request.Approval.ApprovalId);
        command.Parameters.AddWithValue("proposal", request.ProposalId);
        command.Parameters.AddWithValue("version", request.ArtifactVersion);
    }

    private static void EnsureExactRetry(
        ToolPromotionRequest attempted,
        ToolPromotionRequest accepted)
    {
        if (attempted != accepted)
        {
            throw new InvalidOperationException(
                $"Tool promotion '{attempted.PromotionId}' conflicts with its stored value.");
        }
    }
}
