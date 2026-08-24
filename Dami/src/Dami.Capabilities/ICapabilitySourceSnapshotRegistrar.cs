namespace Dami.Capabilities;

/// <summary>Atomically replaces every capability published by one source.</summary>
public interface ICapabilitySourceSnapshotRegistrar : ICapabilityBatchRegistrar
{
    /// <summary>Publishes the complete current snapshot for one capability source.</summary>
    void ReplaceSourceSnapshot(
        CapabilitySource source,
        IReadOnlyList<CapabilityEntry> entries);
}
