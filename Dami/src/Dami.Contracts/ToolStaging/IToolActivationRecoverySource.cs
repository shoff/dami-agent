namespace Dami.Contracts.ToolStaging;

/// <summary>Finds approved exact tools that must be present after runtime startup.</summary>
public interface IToolActivationRecoverySource
{
    /// <summary>Returns a bounded deterministic recovery batch.</summary>
    Task<IReadOnlyList<ToolActivationRecoveryItem>> FindAsync(
        int limit,
        CancellationToken cancellationToken);
}
