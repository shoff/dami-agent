namespace Dami.Contracts.Approvals;

/// <summary>The one approval contract every interface answers through (charter §10.2).</summary>
public interface IApprovalService
{
    /// <summary>Files a request. The caller's action stays blocked until resolution.</summary>
    Task RequestAsync(ApprovalRequest request, CancellationToken cancellationToken);

    /// <summary>Pending requests, oldest first.</summary>
    IAsyncEnumerable<ApprovalRequest> PendingAsync(CancellationToken cancellationToken);

    /// <summary>Resolves one request. Only a Pending request can be resolved.</summary>
    /// <returns>False if the request was not pending (already resolved, or unknown).</returns>
    Task<bool> ResolveAsync(
        Guid approvalId,
        ApprovalStatus resolution,
        string? note,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken);

    /// <summary>Reads one request.</summary>
    Task<ApprovalRequest?> FindAsync(Guid approvalId, CancellationToken cancellationToken);
}
