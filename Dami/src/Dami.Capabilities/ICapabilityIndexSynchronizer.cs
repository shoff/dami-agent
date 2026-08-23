namespace Dami.Capabilities;

/// <summary>Keeps the derived capability-description index aligned with the registry.</summary>
public interface ICapabilityIndexSynchronizer
{
    /// <summary>Indexes changed entries and removes entries absent from the registry snapshot.</summary>
    Task<CapabilityIndexSyncResult> SynchronizeAsync(CancellationToken cancellationToken);
}
