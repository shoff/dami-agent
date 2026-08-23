namespace Dami.Capabilities;

/// <summary>Provides point-in-time capability inventories for derived indexing.</summary>
public interface ICapabilityInventory
{
    /// <summary>Snapshots every registered capability in stable identifier order.</summary>
    IReadOnlyList<CapabilityEntry> Snapshot();
}
