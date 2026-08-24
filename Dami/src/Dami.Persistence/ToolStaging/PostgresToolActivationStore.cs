using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.ToolStaging;

/// <summary>Append-only terminal outcomes for approved exact-tool publication.</summary>
public sealed class PostgresToolActivationStore : IToolActivationStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string eventsTable;
    private readonly string table;
    private readonly string promotionsTable;
    private readonly string approvalsTable;

    /// <summary>Creates the PostgreSQL activation-outcome store.</summary>
    public PostgresToolActivationStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        string schema = options.Value.SchemaName;
        this.approvalsTable = $"{schema}.approvals";
        this.eventsTable = $"{schema}.execution_events";
        this.promotionsTable = $"{schema}.tool_promotions";
        this.table = $"{schema}.tool_activation_outcomes";
    }

    /// <inheritdoc />
    public async Task<ToolActivationOutcome> RecordAsync(
        ToolActivationOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ToolActivationOutcome normalized = Normalize(outcome);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await this.InsertAsync(connection, transaction, normalized, cancellationToken)
            .ConfigureAwait(false);
        ToolActivationOutcome accepted = await this.FindByIdAsync(
            connection, transaction, normalized.ActivationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tool activation outcome could not be reloaded.");
        if (accepted != normalized)
        {
            throw new InvalidOperationException(
                $"Tool activation '{outcome.ActivationId}' conflicts with its stored value.");
        }

        (Guid traceId, ExecutionOrigin origin, string resource) = await this.FindProvenanceAsync(
            connection, transaction, accepted.PromotionId, cancellationToken).ConfigureAwait(false);
        await ExecutionEventCommand.AppendExactAsync(
            connection,
            transaction,
            this.eventsTable,
            ToolActivationEventFactory.Terminal(accepted, traceId, origin, resource),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    private static ToolActivationOutcome Normalize(ToolActivationOutcome outcome)
    {
        return new ToolActivationOutcome(
            outcome.ActivationId,
            outcome.PromotionId,
            outcome.VerificationId,
            outcome.Status,
            outcome.FailureCode,
            PostgresTimestamp.Normalize(outcome.OccurredAt));
    }

    /// <inheritdoc />
    public async Task<ToolActivationOutcome?> FindActivatedAsync(
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        if (promotionId == Guid.Empty)
        {
            throw new ArgumentException("A promotion identifier cannot be empty.", nameof(promotionId));
        }

        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select activation_id, promotion_id, verification_id, status, failure_code, occurred_at
              from {this.table}
             where promotion_id = @promotion and status = 'Activated';
            """, connection);
        command.Parameters.AddWithValue("promotion", promotionId);
        return await ReadOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolActivationOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            insert into {this.table}
                (activation_id, promotion_id, verification_id, status, failure_code, occurred_at)
            values (@activation, @promotion, @verification, @status, @failure, @at)
            on conflict (activation_id) do nothing;
            """, connection, transaction);
        AddParameters(command, outcome);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolActivationOutcome?> FindByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid activationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select activation_id, promotion_id, verification_id, status, failure_code, occurred_at
              from {this.table}
             where activation_id = @activation;
            """, connection, transaction);
        command.Parameters.AddWithValue("activation", activationId);
        return await ReadOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid TraceId, ExecutionOrigin Origin, string Resource)> FindProvenanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select approval.trace_id, approval.origin, approval.resource
              from {this.promotionsTable} promotion
              join {this.approvalsTable} approval
                on approval.approval_id = promotion.approval_id
             where promotion.promotion_id = @promotion;
            """, connection, transaction);
        command.Parameters.AddWithValue("promotion", promotionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The tool promotion could not be reloaded.");
        }

        return (reader.GetGuid(0), Enum.Parse<ExecutionOrigin>(reader.GetString(1)), reader.GetString(2));
    }

    private static async Task<ToolActivationOutcome?> ReadOneAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        bool failureNull = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false);
        return new ToolActivationOutcome(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            Enum.Parse<ToolActivationStatus>(reader.GetString(3)),
            failureNull ? null : reader.GetString(4),
            await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                .ConfigureAwait(false));
    }

    private static void AddParameters(NpgsqlCommand command, ToolActivationOutcome outcome)
    {
        command.Parameters.AddWithValue("activation", outcome.ActivationId);
        command.Parameters.AddWithValue("promotion", outcome.PromotionId);
        command.Parameters.AddWithValue("verification", outcome.VerificationId);
        command.Parameters.AddWithValue("status", outcome.Status.ToString());
        command.Parameters.AddWithValue("failure", (object?)outcome.FailureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("at", outcome.OccurredAt);
    }
}
