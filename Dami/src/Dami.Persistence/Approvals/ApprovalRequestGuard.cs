using Dami.Contracts.Approvals;

namespace Dami.Persistence.Approvals;

/// <summary>Domain invariants shared by approval persistence aggregates.</summary>
internal static class ApprovalRequestGuard
{
    public static void EnsurePending(ApprovalRequest request, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status != ApprovalStatus.Pending
            || request.ResolvedAt is not null
            || request.ResolvedNote is not null)
        {
            throw new ArgumentException(
                "A new approval request must be unresolved and pending.", parameterName);
        }
    }
}
