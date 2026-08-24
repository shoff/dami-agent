using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedToolPromotionWorkflowTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "dami-promotion-" + Guid.NewGuid().ToString("N")))
        .FullName;

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Fact]
    public async Task VerifyAsync_Should_Record_Exact_Evidence_Once_And_Clean_Scratch_Async()
    {
        StagedToolProposal proposal = CreateProposal();
        var proposals = new ProposalStore(proposal);
        var verifications = new VerificationStore();
        var verifier = new StubVerifier();
        var workflow = new SandboxedToolPromotionWorkflow(
            this.root, proposals, verifications, new PromotionStore(), verifier,
            new StubTimeProvider(DateTimeOffset.UnixEpoch));

        ToolVerificationRecord first = await workflow.VerifyAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);
        ToolVerificationRecord second = await workflow.VerifyAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, verifier.CallCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.root));
    }

    [Fact]
    public async Task RequestPromotionAsync_Should_Require_Verification_And_Reuse_One_Request_Async()
    {
        StagedToolProposal proposal = CreateProposal();
        var promotions = new PromotionStore();
        var workflow = new SandboxedToolPromotionWorkflow(
            this.root, new ProposalStore(proposal), new VerificationStore(), promotions,
            new StubVerifier(), new StubTimeProvider(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RequestPromotionAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None));
        await workflow.VerifyAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);
        ToolPromotionRequest first = await workflow.RequestPromotionAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);
        ToolPromotionRequest second = await workflow.RequestPromotionAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, promotions.RequestCount);
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "promotable", "Promote exact bytes.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["promotion"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "Verification precedes approval.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
    }

    private sealed class StubVerifier : IToolArtifactVerifier
    {
        public int CallCount { get; private set; }

        public Task<VerifiedToolArtifact> VerifyAsync(
            ToolProposalArtifact artifact,
            string scratchDirectory,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            Directory.CreateDirectory(scratchDirectory);
            return Task.FromResult(new VerifiedToolArtifact(
                artifact.Version, Path.Combine(scratchDirectory, "Tool.dll"),
                new string('a', 64), "tests_passed=1"));
        }
    }

    private sealed class ProposalStore(StagedToolProposal proposal) : IToolProposalStore
    {
        public Task<StagedToolProposal> StageAsync(
            StagedToolProposal value,
            CancellationToken cancellationToken) => Task.FromResult(value);

        public Task<StagedToolProposal?> FindAsync(
            Guid proposalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StagedToolProposal?>(
                proposalId == proposal.Request.ProposalId ? proposal : null);

        public Task<IReadOnlyList<ToolProposalSummary>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ToolProposalSummary>>([]);
    }

    private sealed class VerificationStore : IToolVerificationStore
    {
        private ToolVerificationRecord? record;

        public Task<ToolVerificationRecord> RecordAsync(
            ToolVerificationRecord value,
            CancellationToken cancellationToken)
        {
            this.record ??= value;
            return Task.FromResult(this.record);
        }

        public Task<ToolVerificationRecord?> FindAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken) => Task.FromResult(this.record);
    }

    private sealed class PromotionStore : IToolPromotionStore
    {
        private ToolPromotionRequest? request;

        public int RequestCount { get; private set; }

        public Task<ToolPromotionRequest> RequestAsync(
            ToolPromotionRequest request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;
            this.request = request;
            return Task.FromResult(request);
        }

        public Task<ToolPromotionRequest?> FindByApprovalAsync(
            Guid approvalId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                this.request?.Approval.ApprovalId == approvalId ? this.request : null);
    }

    private sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
