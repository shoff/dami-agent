using System.Security.Cryptography;
using Dami.Contracts.Approvals;
using Dami.Contracts.Events;

namespace Dami.Persistence.Approvals;

/// <summary>Creates stable execution events for approval lifecycle transitions.</summary>
internal static class ApprovalExecutionEventFactory
{
    private const byte REQUESTED_EVENT = 1;
    private const byte RESOLVED_EVENT = 2;

    public static ExecutionEvent Requested(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ExecutionEvent(
            EventId(request.ApprovalId, REQUESTED_EVENT),
            request.TraceId,
            request.ApprovalId,
            request.ParentSpanId,
            request.Origin,
            request.RequestedBy,
            ExecutionEventType.ApprovalRequested,
            ExecutionStatus.Waiting,
            request.RequestedAt,
            $"Approval requested: {request.Action}");
    }

    public static ExecutionEvent Resolved(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status == ApprovalStatus.Pending || request.ResolvedAt is null)
        {
            throw new ArgumentException("A resolved approval event requires a resolution.", nameof(request));
        }

        return new ExecutionEvent(
            EventId(request.ApprovalId, RESOLVED_EVENT),
            request.TraceId,
            request.ApprovalId,
            request.ParentSpanId,
            request.Origin,
            "approval-service",
            ExecutionEventType.ApprovalResolved,
            request.Status == ApprovalStatus.Approved
                ? ExecutionStatus.Succeeded
                : ExecutionStatus.Cancelled,
            request.ResolvedAt.Value,
            $"Approval {request.Status}: {request.Action}");
    }

    private static Guid EventId(Guid approvalId, byte eventKind)
    {
        Span<byte> input = stackalloc byte[17];
        approvalId.TryWriteBytes(input);
        input[^1] = eventKind;
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
