using Dami.Contracts.Approvals;

namespace Dami.Contracts.FilePatches;

/// <summary>Durable storage for immutable approval-gated file patch proposals.</summary>
public interface IFilePatchProposalStore
{
    /// <summary>Atomically stores one pending approval and its proposal without changing the target.</summary>
    Task CreateAsync(
        ApprovalRequest approval,
        FilePatchProposal proposal,
        CancellationToken cancellationToken);

    /// <summary>Finds the proposal gated by an approval.</summary>
    Task<FilePatchProposal?> FindByApprovalAsync(Guid approvalId, CancellationToken cancellationToken);
}
