using System.Globalization;
using Dami.Contracts.Approvals;

namespace Dami.Contracts.ToolStaging;

/// <summary>A human promotion request pinned to one immutable staged artifact.</summary>
public sealed record ToolPromotionRequest
{
    /// <summary>The component identity authorized to request tool promotion.</summary>
    public const string REQUESTED_BY = "tools:promotion";

    /// <summary>The approval scope reserved for tool promotion.</summary>
    public const string SCOPE = "tool-promotion";

    /// <summary>Creates one version-pinned promotion request.</summary>
    public ToolPromotionRequest(
        Guid promotionId,
        Guid proposalId,
        string artifactVersion,
        ApprovalRequest approval)
    {
        ArgumentNullException.ThrowIfNull(artifactVersion);
        ArgumentNullException.ThrowIfNull(approval);
        ToolArtifactVersion.Validate(artifactVersion, nameof(artifactVersion));
        if (promotionId == Guid.Empty || proposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tool promotions require non-empty promotion and proposal identifiers.");
        }

        if (approval.Status != ApprovalStatus.Pending
            || approval.ResolvedAt is not null
            || approval.ResolvedNote is not null)
        {
            throw new ArgumentException(
                "A tool promotion requires an unresolved pending approval.", nameof(approval));
        }

        if (approval.ApprovalId == Guid.Empty
            || approval.TraceId == Guid.Empty
            || approval.ParentSpanId is null
            || !string.Equals(approval.RequestedBy, REQUESTED_BY, StringComparison.Ordinal)
            || !string.Equals(approval.Scope, SCOPE, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A tool promotion approval requires complete promotion provenance.",
                nameof(approval));
        }

        if (!string.Equals(
            approval.Resource, Resource(proposalId, artifactVersion), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The approval resource must pin the exact proposed artifact.", nameof(approval));
        }

        this.PromotionId = promotionId;
        this.ProposalId = proposalId;
        this.ArtifactVersion = artifactVersion;
        this.Approval = approval;
    }

    /// <summary>Gets the retry-stable promotion identifier.</summary>
    public Guid PromotionId { get; }

    /// <summary>Gets the immutable proposal being reviewed.</summary>
    public Guid ProposalId { get; }

    /// <summary>Gets the exact staged artifact version being reviewed.</summary>
    public string ArtifactVersion { get; }

    /// <summary>Gets the single-resolution human approval.</summary>
    public ApprovalRequest Approval { get; }

    /// <summary>Creates the approval resource for one exact staged version.</summary>
    public static string Resource(Guid proposalId, string artifactVersion)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tool-proposal://{proposalId:D}/versions/{artifactVersion}");
    }
}
