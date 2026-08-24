namespace Dami.Contracts.ToolStaging;

/// <summary>Persists inert tool proposals and their canonical staging events.</summary>
public interface IToolProposalStore
{
    /// <summary>Stages one proposal idempotently and returns its canonical stored value.</summary>
    Task<StagedToolProposal> StageAsync(
        StagedToolProposal proposal,
        CancellationToken cancellationToken);

    /// <summary>Finds one proposal by retry-stable identifier.</summary>
    Task<StagedToolProposal?> FindAsync(
        Guid proposalId,
        CancellationToken cancellationToken);

    /// <summary>Lists compact proposal metadata newest first.</summary>
    Task<IReadOnlyList<ToolProposalSummary>> ListAsync(
        int limit,
        CancellationToken cancellationToken);
}
