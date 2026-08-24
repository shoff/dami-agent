using Dami.Contracts.Approvals;
using Dami.Contracts.FilePatches;
using Dami.Persistence.Approvals;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.FilePatches;

/// <summary>Immutable file patch proposals in PostgreSQL.</summary>
public sealed class PostgresFilePatchProposalStore : IFilePatchProposalStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string approvalsTable;
    private readonly string table;

    /// <summary>Creates the proposal store.</summary>
    public PostgresFilePatchProposalStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        var schema = options.Value.SchemaName;
        this.approvalsTable = $"{schema}.approvals";
        this.table = $"{schema}.file_patch_proposals";
    }

    /// <inheritdoc />
    public async Task CreateAsync(
        ApprovalRequest approval,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        ValidateAggregate(approval, proposal);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await this.InsertApprovalAsync(
            connection, transaction, approval, cancellationToken).ConfigureAwait(false);
        await this.InsertProposalAsync(
            connection, transaction, proposal, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FilePatchProposal?> FindByApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindAsync(
            connection, null, "approval_id", approvalId, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            ApprovalRequestCommand.InsertSql(this.approvalsTable),
            connection,
            transaction);
        ApprovalRequestCommand.AddParameters(command, approval);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
        {
            await this.EnsureExactApprovalReplayAsync(
                connection, transaction, approval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InsertProposalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.table}
                (proposal_id, approval_id, trace_id, span_id, relative_path,
                 replacement_content, replacement_sha256, expected_sha256, created_at)
            values (@proposal, @approval, @trace, @span, @path, @content, @replacement, @expected, @at)
            on conflict (proposal_id) do nothing;
            """,
            connection,
            transaction);
        AddParameters(command, proposal);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
        {
            await this.EnsureExactReplayAsync(
                connection, transaction, proposal, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<FilePatchProposal?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string column,
        Guid value,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select proposal_id, approval_id, trace_id, span_id, relative_path,
                   replacement_content, replacement_sha256, expected_sha256, created_at
              from {this.table}
             where {column} = @value;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task EnsureExactReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        var stored = await this.FindAsync(
            connection,
            transaction,
            "proposal_id",
            proposal.ProposalId,
            cancellationToken).ConfigureAwait(false);
        if (stored != proposal)
        {
            throw new InvalidOperationException(
                $"File patch proposal '{proposal.ProposalId}' conflicts with its immutable stored value.");
        }
    }

    private async Task EnsureExactApprovalReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select trace_id = @trace
               and requested_by = @by
               and action = @action
               and scope = @scope
               and resource = @resource
               and status = @status
               and requested_at = @at
               and resolved_at is null
               and resolved_note is null
               and expires_at is not distinct from @expires
              from {this.approvalsTable}
             where approval_id = @id;
            """,
            connection,
            transaction);
        ApprovalRequestCommand.AddParameters(command, approval);
        var exact = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (exact is not true)
        {
            throw new InvalidOperationException(
                $"Approval '{approval.ApprovalId}' conflicts with its immutable requested value.");
        }
    }

    private static void ValidateAggregate(ApprovalRequest approval, FilePatchProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(proposal);
        if (approval.Status != ApprovalStatus.Pending
            || approval.ResolvedAt is not null
            || approval.ResolvedNote is not null)
        {
            throw new ArgumentException("File patch approvals must be unresolved and pending.", nameof(approval));
        }

        if (approval.ApprovalId != proposal.ApprovalId
            || approval.TraceId != proposal.TraceId
            || !string.Equals(approval.Resource, proposal.RelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("Approval provenance and resource must match the proposal.", nameof(approval));
        }
    }

    private static void AddParameters(NpgsqlCommand command, FilePatchProposal proposal)
    {
        command.Parameters.AddWithValue("proposal", proposal.ProposalId);
        command.Parameters.AddWithValue("approval", proposal.ApprovalId);
        command.Parameters.AddWithValue("trace", proposal.TraceId);
        command.Parameters.AddWithValue("span", proposal.SpanId);
        command.Parameters.AddWithValue("path", proposal.RelativePath);
        command.Parameters.AddWithValue("content", proposal.ReplacementContent);
        command.Parameters.AddWithValue("replacement", proposal.ReplacementSha256);
        command.Parameters.AddWithValue("expected", (object?)proposal.ExpectedSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("at", proposal.CreatedAt);
    }

    private static async Task<FilePatchProposal> ReadAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var expectedNull = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false);
        return new FilePatchProposal(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6),
            expectedNull ? null : reader.GetString(7),
            await reader.GetFieldValueAsync<DateTimeOffset>(8, cancellationToken).ConfigureAwait(false));
    }
}
