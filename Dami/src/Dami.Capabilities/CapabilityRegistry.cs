using System.Collections.Concurrent;

namespace Dami.Capabilities;

/// <summary>Provides a source-neutral lookup surface for registered capabilities.</summary>
public sealed class CapabilityRegistry : ICapabilityCatalog, ICapabilityInventory, ICapabilityRegistrar
{
    private readonly ConcurrentDictionary<Guid, CapabilityEntry> entries = [];

    /// <summary>Registers a capability under its stable identifier.</summary>
    /// <param name="entry">The normalized capability registration.</param>
    public void Register(CapabilityEntry entry)
    {
        if (!this.entries.TryAdd(entry.CapabilityId, entry))
        {
            throw new InvalidOperationException(
                $"Capability '{entry.CapabilityId}' is already registered.");
        }
    }

    /// <summary>Finds a registered capability by its stable identifier.</summary>
    /// <param name="capabilityId">The stable capability identifier.</param>
    /// <returns>The matching registration, or <see langword="null"/>.</returns>
    public CapabilityEntry? Find(Guid capabilityId)
    {
        return this.entries.TryGetValue(capabilityId, out CapabilityEntry? entry) ? entry : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<CapabilityEntry> Snapshot()
    {
        return Array.AsReadOnly(this.entries.Values.OrderBy(entry => entry.CapabilityId).ToArray());
    }
}
