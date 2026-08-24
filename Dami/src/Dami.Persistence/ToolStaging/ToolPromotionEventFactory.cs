using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Persistence.ToolStaging;

internal static class ToolPromotionEventFactory
{
    public static ExecutionEvent Requested(ToolPromotionRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["approval_id"] = request.Approval.ApprovalId.ToString("D"),
            ["proposal_id"] = request.ProposalId.ToString("D"),
            ["artifact_version"] = request.ArtifactVersion,
        };
        return new ExecutionEvent(
            request.PromotionId,
            request.Approval.TraceId,
            request.PromotionId,
            request.Approval.ParentSpanId,
            request.Approval.Origin,
            ToolPromotionRequest.REQUESTED_BY,
            ExecutionEventType.ToolPromotionRequested,
            ExecutionStatus.Waiting,
            request.Approval.RequestedAt,
            "tool promotion awaiting human approval",
            request.Approval.Resource,
            metadata);
    }
}
