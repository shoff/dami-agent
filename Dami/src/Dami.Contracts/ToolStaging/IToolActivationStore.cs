namespace Dami.Contracts.ToolStaging;

/// <summary>Persists terminal exact-tool activation outcomes.</summary>
public interface IToolActivationStore
{
    /// <summary>Records one terminal outcome idempotently with its event.</summary>
    Task<ToolActivationOutcome> RecordAsync(
        ToolActivationOutcome outcome,
        CancellationToken cancellationToken);

    /// <summary>Finds the successful activation for a promotion, if one exists.</summary>
    Task<ToolActivationOutcome?> FindActivatedAsync(
        Guid promotionId,
        CancellationToken cancellationToken);
}
