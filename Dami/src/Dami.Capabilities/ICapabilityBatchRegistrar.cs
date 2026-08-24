namespace Dami.Capabilities;

/// <summary>Atomically publishes a prepared capability set or publishes none of it.</summary>
public interface ICapabilityBatchRegistrar : ICapabilityRegistrar
{
    /// <summary>Registers every entry after validating the complete set.</summary>
    void RegisterBatch(IReadOnlyList<CapabilityEntry> entries);
}
