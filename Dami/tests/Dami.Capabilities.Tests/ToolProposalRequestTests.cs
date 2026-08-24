using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Tests;

public sealed class ToolProposalRequestTests
{
    private static readonly DateTimeOffset proposedAt =
        new(2026, 8, 24, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Should_Create_A_Trace_Owned_Version_Pinned_Proposal()
    {
        var proposalId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var spanId = Guid.NewGuid();
        var parentSpanId = Guid.NewGuid();
        ToolProposalArtifact artifact = CreateArtifact();
        string version = artifact.Version;

        var request = new ToolProposalRequest(
            proposalId, traceId, spanId, parentSpanId,
            ExecutionOrigin.SelfAudit, artifact);
        var staged = new StagedToolProposal(request, version, proposedAt);

        Assert.Equal(
            (proposalId, traceId, spanId, parentSpanId, ExecutionOrigin.SelfAudit,
                artifact.Schema.CapabilityId, version, proposedAt),
            (staged.Request.ProposalId, staged.Request.TraceId, staged.Request.SpanId,
                staged.Request.ParentSpanId, staged.Request.Origin,
                staged.Request.Artifact.Schema.CapabilityId,
                staged.ArtifactVersion, staged.ProposedAt));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Version_That_Does_Not_Match_The_Artifact()
    {
        ToolProposalArtifact artifact = CreateArtifact();
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);

        Assert.Throws<ArgumentException>(
            () => new StagedToolProposal(request, new string('f', 64), proposedAt));
    }

    private static ToolProposalArtifact CreateArtifact()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "review-tool", "Review a bounded artifact.", parameters.RootElement);
        return new ToolProposalArtifact(
            schema, ["review"],
            new Dictionary<string, string> { ["ReviewTool.cs"] = "source" },
            new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests" },
            "A rationale.", [Guid.NewGuid()], ToolExecutionProfile.ReadOnly);
    }
}
