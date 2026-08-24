using Dami.Contracts.Approvals;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Tests;

public sealed class ToolPromotionRequestTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Should_Pin_One_Pending_Approval_To_An_Exact_Artifact()
    {
        var proposalId = Guid.NewGuid();
        string version = new('a', 64);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "tools:promotion",
            "Promote the reviewed tool artifact.", "tool-promotion",
            Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(proposalId, version), at,
            origin: ExecutionOrigin.UserTurn, parentSpanId: Guid.NewGuid());

        var promotion = new Dami.Contracts.ToolStaging.ToolPromotionRequest(
            Guid.NewGuid(), proposalId, version, approval);

        Assert.Equal(
            (proposalId, version, approval.ApprovalId, approval.TraceId,
                ApprovalStatus.Pending, approval.Resource),
            (promotion.ProposalId, promotion.ArtifactVersion,
                promotion.Approval.ApprovalId, promotion.Approval.TraceId,
                promotion.Approval.Status, promotion.Approval.Resource));
    }

    [Fact]
    public void Constructor_Should_Reject_Approval_For_A_Different_Artifact()
    {
        var proposalId = Guid.NewGuid();
        string version = new('a', 64);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
            "tool-promotion",
            Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(
                Guid.NewGuid(), version),
            at);

        Assert.Throws<ArgumentException>(() =>
            new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                Guid.NewGuid(), proposalId, version, approval));
    }

    [Fact]
    public void Constructor_Should_Reject_An_Already_Resolved_Approval()
    {
        var proposalId = Guid.NewGuid();
        string version = new('a', 64);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
            "tool-promotion",
            Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(proposalId, version),
            at, ApprovalStatus.Approved, at.AddMinutes(1), "approved");

        Assert.Throws<ArgumentException>(() =>
            new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                Guid.NewGuid(), proposalId, version, approval));
    }

    [Fact]
    public void Constructor_Should_Reject_Empty_Promotion_Or_Proposal_Ids()
    {
        var proposalId = Guid.NewGuid();
        string version = new('a', 64);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
            "tool-promotion",
            Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(proposalId, version), at);

        Assert.Throws<ArgumentException>(() =>
            new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                Guid.Empty, proposalId, version, approval));
        Assert.Throws<ArgumentException>(() =>
            new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                Guid.NewGuid(), Guid.Empty, version, approval));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Noncanonical_Artifact_Version()
    {
        var proposalId = Guid.NewGuid();
        const string version = "not-a-version";
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
            "tool-promotion",
            Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(proposalId, version), at);

        Assert.Throws<ArgumentException>(() =>
            new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                Guid.NewGuid(), proposalId, version, approval));
    }

    [Fact]
    public void Constructor_Should_Reject_Approval_Without_Required_Promotion_Provenance()
    {
        var proposalId = Guid.NewGuid();
        string version = new('a', 64);
        string resource = Dami.Contracts.ToolStaging.ToolPromotionRequest.Resource(
            proposalId, version);
        ApprovalRequest[] malformed =
        [
            new ApprovalRequest(
                Guid.Empty, Guid.NewGuid(), "tools:promotion", "Promote tool.",
                "tool-promotion", resource, at, parentSpanId: Guid.NewGuid()),
            new ApprovalRequest(
                Guid.NewGuid(), Guid.Empty, "tools:promotion", "Promote tool.",
                "tool-promotion", resource, at, parentSpanId: Guid.NewGuid()),
            new ApprovalRequest(
                Guid.NewGuid(), Guid.NewGuid(), "another-component", "Promote tool.",
                "tool-promotion", resource, at, parentSpanId: Guid.NewGuid()),
            new ApprovalRequest(
                Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
                "another-scope", resource, at, parentSpanId: Guid.NewGuid()),
            new ApprovalRequest(
                Guid.NewGuid(), Guid.NewGuid(), "tools:promotion", "Promote tool.",
                "tool-promotion", resource, at),
        ];

        foreach (ApprovalRequest approval in malformed)
        {
            Assert.Throws<ArgumentException>(() =>
                new Dami.Contracts.ToolStaging.ToolPromotionRequest(
                    Guid.NewGuid(), proposalId, version, approval));
        }
    }
}
