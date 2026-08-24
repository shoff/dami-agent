using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Persistence.ToolStaging;

internal static class ToolVerificationEventFactory
{
    public static ExecutionEvent Verified(
        ToolVerificationRecord record,
        Guid traceId,
        Guid proposalSpanId,
        ExecutionOrigin origin)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proposal_id"] = record.ProposalId.ToString("D"),
            ["artifact_version"] = record.ArtifactVersion,
            ["assembly_sha256"] = record.AssemblySha256,
        };
        return new ExecutionEvent(
            record.VerificationId,
            traceId,
            record.VerificationId,
            proposalSpanId,
            origin,
            ToolVerificationRecord.VERIFIED_BY,
            ExecutionEventType.ToolVerified,
            ExecutionStatus.Succeeded,
            record.VerifiedAt,
            "sandboxed tool verification succeeded",
            ToolPromotionRequest.Resource(record.ProposalId, record.ArtifactVersion),
            metadata);
    }
}
