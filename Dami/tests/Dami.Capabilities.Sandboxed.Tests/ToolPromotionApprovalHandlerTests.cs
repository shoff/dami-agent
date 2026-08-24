using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class ToolPromotionApprovalHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Activate_Only_The_Approved_Exact_Version_Async()
    {
        StagedToolProposal proposal = CreateProposal();
        ToolVerificationRecord verification = CreateVerification(proposal);
        ToolPromotionRequest promotion = CreatePromotion(proposal);
        var coordinator = new Coordinator();
        var handler = new ToolPromotionApprovalHandler(
            new ApprovalService(Approved(promotion.Approval)), new PromotionStore(promotion),
            new ProposalStore(proposal), new VerificationStore(verification),
            new ActivationStore(), coordinator);

        string result = await handler.ExecuteAsync(
            promotion.Approval, CancellationToken.None);

        Assert.True(handler.CanExecute(promotion.Approval));
        Assert.Equal(proposal.ArtifactVersion, coordinator.Item!.Proposal.ArtifactVersion);
        Assert.Contains(proposal.Request.Artifact.Schema.CapabilityId.ToString("D"), result,
            StringComparison.Ordinal);
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "approved-tool", "Run approved bytes.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["approval"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "The human reviewed this exact version.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
    }

    private static ToolVerificationRecord CreateVerification(StagedToolProposal proposal)
    {
        return new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            new string('a', 64), "tests_passed=1", DateTimeOffset.UnixEpoch);
    }

    private static ToolPromotionRequest CreatePromotion(StagedToolProposal proposal)
    {
        var approval = new ApprovalRequest(
            Guid.NewGuid(), proposal.Request.TraceId, ToolPromotionRequest.REQUESTED_BY,
            "promote approved tool", ToolPromotionRequest.SCOPE,
            ToolPromotionRequest.Resource(
                proposal.Request.ProposalId, proposal.ArtifactVersion),
            DateTimeOffset.UnixEpoch, origin: proposal.Request.Origin,
            parentSpanId: proposal.Request.SpanId);
        return new ToolPromotionRequest(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion, approval);
    }

    private static ApprovalRequest Approved(ApprovalRequest pending)
    {
        return new ApprovalRequest(
            pending.ApprovalId, pending.TraceId, pending.RequestedBy, pending.Action,
            pending.Scope, pending.Resource, pending.RequestedAt, ApprovalStatus.Approved,
            DateTimeOffset.UnixEpoch.AddMinutes(1), "approved", pending.ExpiresAt,
            pending.Origin, pending.ParentSpanId);
    }

    private sealed class Coordinator : IToolActivationCoordinator
    {
        public ToolActivationRecoveryItem? Item { get; private set; }

        public Task ActivateAsync(
            ToolActivationRecoveryItem item,
            CancellationToken cancellationToken)
        {
            this.Item = item;
            return Task.CompletedTask;
        }
    }

    private sealed class ApprovalService(ApprovalRequest approval) : IApprovalService
    {
        public Task RequestAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<ApprovalRequest> PendingAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> ResolveAsync(
            Guid approvalId,
            ApprovalStatus resolution,
            string? note,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ApprovalRequest?> FindAsync(
            Guid approvalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalRequest?>(
                approvalId == approval.ApprovalId ? approval : null);
    }

    private sealed class PromotionStore(ToolPromotionRequest promotion) : IToolPromotionStore
    {
        public Task<ToolPromotionRequest> RequestAsync(
            ToolPromotionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(request);

        public Task<ToolPromotionRequest?> FindByApprovalAsync(
            Guid approvalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ToolPromotionRequest?>(
                approvalId == promotion.Approval.ApprovalId ? promotion : null);
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

    private sealed class VerificationStore(ToolVerificationRecord verification)
        : IToolVerificationStore
    {
        public Task<ToolVerificationRecord> RecordAsync(
            ToolVerificationRecord record,
            CancellationToken cancellationToken) => Task.FromResult(record);

        public Task<ToolVerificationRecord?> FindAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult<ToolVerificationRecord?>(verification);
    }

    private sealed class ActivationStore : IToolActivationStore
    {
        public Task<ToolActivationOutcome> RecordAsync(
            ToolActivationOutcome outcome,
            CancellationToken cancellationToken) => Task.FromResult(outcome);

        public Task<ToolActivationOutcome?> FindActivatedAsync(
            Guid promotionId,
            CancellationToken cancellationToken) => Task.FromResult<ToolActivationOutcome?>(null);
    }
}
