using System.Globalization;
using Dami.Contracts.Approvals;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Activates the exact tool version behind an authoritative Approved request.</summary>
public sealed class ToolPromotionApprovalHandler : IApprovalExecutionHandler
{
    private readonly IToolActivationStore activations;
    private readonly IApprovalService approvals;
    private readonly IToolActivationCoordinator coordinator;
    private readonly IToolPromotionStore promotions;
    private readonly IToolProposalStore proposals;
    private readonly IToolVerificationStore verifications;

    /// <summary>Creates the approved promotion handler.</summary>
    public ToolPromotionApprovalHandler(
        IApprovalService approvals,
        IToolPromotionStore promotions,
        IToolProposalStore proposals,
        IToolVerificationStore verifications,
        IToolActivationStore activations,
        IToolActivationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(promotions);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(verifications);
        ArgumentNullException.ThrowIfNull(activations);
        ArgumentNullException.ThrowIfNull(coordinator);
        this.approvals = approvals;
        this.promotions = promotions;
        this.proposals = proposals;
        this.verifications = verifications;
        this.activations = activations;
        this.coordinator = coordinator;
    }

    /// <inheritdoc />
    public bool CanExecute(ApprovalRequest approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return string.Equals(
                approval.RequestedBy, ToolPromotionRequest.REQUESTED_BY, StringComparison.Ordinal)
            && string.Equals(approval.Scope, ToolPromotionRequest.SCOPE, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ApprovalRequest authoritative = await this.FindApprovedAsync(
            approval.ApprovalId, cancellationToken).ConfigureAwait(false);
        ToolPromotionRequest promotion = await this.FindPromotionAsync(
            authoritative, cancellationToken).ConfigureAwait(false);
        ToolActivationRecoveryItem item = await this.LoadItemAsync(
            promotion, cancellationToken).ConfigureAwait(false);
        await this.coordinator.ActivateAsync(item, cancellationToken).ConfigureAwait(false);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"activated sandboxed capability {item.Proposal.Request.Artifact.Schema.CapabilityId:D} "
            + $"version {item.Proposal.ArtifactVersion}");
    }

    private async Task<ApprovalRequest> FindApprovedAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        ApprovalRequest? approval = await this.approvals.FindAsync(
            approvalId, cancellationToken).ConfigureAwait(false);
        if (approval?.Status != ApprovalStatus.Approved || !this.CanExecute(approval))
        {
            throw new InvalidOperationException(
                $"Tool promotion approval '{approvalId}' is not Approved.");
        }

        return approval;
    }

    private async Task<ToolPromotionRequest> FindPromotionAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        ToolPromotionRequest promotion = await this.promotions.FindByApprovalAsync(
            approval.ApprovalId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The approved tool promotion was not found.");
        if (!string.Equals(
            approval.Resource,
            ToolPromotionRequest.Resource(promotion.ProposalId, promotion.ArtifactVersion),
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("The approval does not pin the stored promotion.");
        }

        return promotion;
    }

    private async Task<ToolActivationRecoveryItem> LoadItemAsync(
        ToolPromotionRequest promotion,
        CancellationToken cancellationToken)
    {
        StagedToolProposal proposal = await this.proposals.FindAsync(
            promotion.ProposalId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The promoted tool proposal was not found.");
        ToolVerificationRecord verification = await this.verifications.FindAsync(
            promotion.ProposalId, promotion.ArtifactVersion, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The promoted tool verification was not found.");
        ToolActivationOutcome? activated = await this.activations.FindActivatedAsync(
            promotion.PromotionId, cancellationToken).ConfigureAwait(false);
        return new ToolActivationRecoveryItem(
            promotion.PromotionId, proposal, verification, activated is not null);
    }
}
