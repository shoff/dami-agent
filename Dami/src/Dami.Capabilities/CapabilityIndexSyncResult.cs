namespace Dami.Capabilities;

/// <summary>Counts of capability vectors changed by one synchronization pass.</summary>
public readonly record struct CapabilityIndexSyncResult(int IndexedCount, int RemovedCount);
