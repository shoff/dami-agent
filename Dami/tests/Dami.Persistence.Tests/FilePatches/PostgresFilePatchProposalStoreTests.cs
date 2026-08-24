using Dami.Contracts.Approvals;
using Dami.Contracts.Events;
using Dami.Contracts.FilePatches;
using Dami.Persistence.Approvals;
using Dami.Persistence.FilePatches;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests.FilePatches;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresFilePatchProposalStoreTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 2, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresFilePatchProposalStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task FindByApprovalAsync_Should_Round_Trip_The_Hash_Pinned_Proposal()
    {
        await this.fixture.ResetAsync();
        var (store, _, _, proposal) = await this.CreateStoredProposalAsync();

        var found = await store.FindByApprovalAsync(proposal.ApprovalId, CancellationToken.None);

        Assert.Equal(proposal, found);
    }

    [Fact]
    public async Task CreateAsync_Should_Round_Trip_The_Approval_Parent_Span()
    {
        await this.fixture.ResetAsync();
        var (_, approvals, approval, _) = await this.CreateStoredProposalAsync();

        var found = await approvals.FindAsync(approval.ApprovalId, CancellationToken.None);

        Assert.Equal(approval.ParentSpanId, found!.ParentSpanId);
        Assert.Equal(approval.Origin, found.Origin);
    }

    [Fact]
    public async Task CreateAsync_Should_Be_Idempotent_For_The_Exact_Proposal()
    {
        await this.fixture.ResetAsync();
        var (store, _, approval, proposal) = await this.CreateStoredProposalAsync();

        await store.CreateAsync(approval, proposal, CancellationToken.None);

        Assert.Equal(proposal, await store.FindByApprovalAsync(
            proposal.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_A_Conflicting_Replay_Without_Mutating_The_Row()
    {
        await this.fixture.ResetAsync();
        var (store, _, approval, proposal) = await this.CreateStoredProposalAsync();
        var conflicting = new FilePatchProposal(
            proposal.ProposalId, proposal.ApprovalId, proposal.TraceId, proposal.SpanId,
            proposal.RelativePath, "different text", FilePatchProposal.HashOf("different text"),
            proposal.ExpectedSha256, proposal.CreatedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(approval, conflicting, CancellationToken.None));

        Assert.Equal(proposal, await store.FindByApprovalAsync(
            proposal.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Converge_Concurrent_Exact_Replays()
    {
        await this.fixture.ResetAsync();
        var (store, _, approval, proposal) = await this.CreateStoredProposalAsync();

        await Task.WhenAll(
            store.CreateAsync(approval, proposal, CancellationToken.None),
            store.CreateAsync(approval, proposal, CancellationToken.None));

        Assert.Equal(proposal, await store.FindByApprovalAsync(
            proposal.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Roll_Back_Approval_When_Proposal_Conflicts()
    {
        await this.fixture.ResetAsync();
        var (store, approvals, _, proposal) = await this.CreateStoredProposalAsync();
        var conflictingApproval = new ApprovalRequest(
            Guid.NewGuid(), proposal.TraceId, "file-patch", "replace file",
            "filesystem", "other.txt", at);
        var conflictingProposal = new FilePatchProposal(
            proposal.ProposalId, conflictingApproval.ApprovalId, proposal.TraceId, proposal.SpanId,
            "other.txt", "other text", FilePatchProposal.HashOf("other text"), null, at);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(
                conflictingApproval, conflictingProposal, CancellationToken.None));

        Assert.Null(await approvals.FindAsync(
            conflictingApproval.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Conflicting_Approval_Replay()
    {
        await this.fixture.ResetAsync();
        var (store, approvals, approval, proposal) = await this.CreateStoredProposalAsync();
        var conflictingApproval = new ApprovalRequest(
            approval.ApprovalId, approval.TraceId, approval.RequestedBy, "different action",
            approval.Scope, approval.Resource, approval.RequestedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(
                conflictingApproval, proposal, CancellationToken.None));

        Assert.Equal(approval, await approvals.FindAsync(
            approval.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task Database_Should_Reject_Mutation_Even_From_The_Ddl_Owner()
    {
        await this.fixture.ResetAsync();
        var (_, _, _, proposal) = await this.CreateStoredProposalAsync();
        await using var command = this.fixture.DataSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.file_patch_proposals "
            + "set replacement_content = 'tampered' where proposal_id = @proposal;");
        command.Parameters.AddWithValue("proposal", proposal.ProposalId);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        Assert.Contains("append-only", exception.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_Should_Grant_The_App_Only_Insert_And_Select()
    {
        await this.fixture.ResetAsync();
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            select has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'select')
               and has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'insert')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'update')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'delete')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'truncate')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'references')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.file_patch_proposals', 'trigger');
            """);

        var leastPrivilege = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(true, leastPrivilege);
    }

    private async Task<(
        PostgresFilePatchProposalStore Store,
        PostgresApprovalService Approvals,
        ApprovalRequest Approval,
        FilePatchProposal Proposal)>
        CreateStoredProposalAsync()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        var approvals = new PostgresApprovalService(
            this.fixture.DataSource, options, NullLogger<PostgresApprovalService>.Instance);
        var spanId = Guid.NewGuid();
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "file-patch", "replace file", "filesystem", "notes.txt", at,
            origin: ExecutionOrigin.UserTurn, parentSpanId: spanId);
        var proposal = new FilePatchProposal(
            Guid.NewGuid(), approval.ApprovalId, approval.TraceId, spanId, "notes.txt",
            "replacement text", FilePatchProposal.HashOf("replacement text"), new string('a', 64), at);
        var store = new PostgresFilePatchProposalStore(this.fixture.DataSource, options);
        await store.CreateAsync(approval, proposal, CancellationToken.None);
        return (store, approvals, approval, proposal);
    }
}
