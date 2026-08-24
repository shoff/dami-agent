namespace Dami.Contracts.ToolStaging;

/// <summary>An approved exact tool artifact that must exist in the live registry.</summary>
public sealed record ToolActivationRecoveryItem
{
    /// <summary>Creates one durable activation-recovery projection.</summary>
    public ToolActivationRecoveryItem(
        Guid promotionId,
        StagedToolProposal proposal,
        ToolVerificationRecord verification,
        bool isActivated)
    {
        if (promotionId == Guid.Empty)
        {
            throw new ArgumentException("A promotion identifier cannot be empty.", nameof(promotionId));
        }

        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.ProposalId != proposal.Request.ProposalId
            || !string.Equals(
                verification.ArtifactVersion, proposal.ArtifactVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Recovery requires verification of the exact staged proposal.",
                nameof(verification));
        }

        this.PromotionId = promotionId;
        this.Proposal = proposal;
        this.Verification = verification;
        this.IsActivated = isActivated;
    }

    /// <summary>Gets the approved promotion identifier.</summary>
    public Guid PromotionId { get; }

    /// <summary>Gets the immutable staged source, tests, schema, and provenance.</summary>
    public StagedToolProposal Proposal { get; }

    /// <summary>Gets the exact durable verification evidence.</summary>
    public ToolVerificationRecord Verification { get; }

    /// <summary>Gets whether durable activation success already exists.</summary>
    public bool IsActivated { get; }
}
