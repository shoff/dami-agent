using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Persistence.ToolStaging;

internal static class ToolActivationEventFactory
{
    public static ExecutionEvent Terminal(
        ToolActivationOutcome outcome,
        Guid traceId,
        ExecutionOrigin origin,
        string resource)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["promotion_id"] = outcome.PromotionId.ToString("D"),
            ["verification_id"] = outcome.VerificationId.ToString("D"),
        };
        if (outcome.FailureCode is not null)
        {
            metadata["failure_code"] = outcome.FailureCode;
        }

        bool succeeded = outcome.Status == ToolActivationStatus.Activated;
        return new ExecutionEvent(
            outcome.ActivationId,
            traceId,
            outcome.ActivationId,
            outcome.PromotionId,
            origin,
            ToolActivationOutcome.ACTIVATED_BY,
            succeeded
                ? ExecutionEventType.ToolActivated
                : ExecutionEventType.ToolActivationFailed,
            succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
            outcome.OccurredAt,
            succeeded ? "sandboxed tool activated" : "sandboxed tool activation failed",
            resource,
            metadata);
    }
}
