using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Persistence.ToolStaging;

internal static class ToolProposalEventFactory
{
    public static ExecutionEvent Proposed(StagedToolProposal proposal)
    {
        ToolProposalRequest request = proposal.Request;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["capability_id"] = request.Artifact.Schema.CapabilityId.ToString("D"),
            ["artifact_version"] = proposal.ArtifactVersion,
            ["execution_profile"] = request.Artifact.ExecutionProfile.ToString(),
        };
        return new ExecutionEvent(
            request.ProposalId, request.TraceId, request.SpanId, request.ParentSpanId,
            request.Origin, "tools:staging", ExecutionEventType.ToolProposed,
            ExecutionStatus.Succeeded, proposal.ProposedAt, "tool proposal staged",
            $"tool-proposal://{request.ProposalId:D}", metadata);
    }
}
