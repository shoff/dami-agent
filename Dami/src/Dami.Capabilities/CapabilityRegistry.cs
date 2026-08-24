using System.Collections.Concurrent;

namespace Dami.Capabilities;

/// <summary>Provides a source-neutral lookup surface for registered capabilities.</summary>
public sealed class CapabilityRegistry :
    ICapabilityCatalog,
    ICapabilityInventory,
    ICapabilityBatchRegistrar
{
    private ConcurrentDictionary<Guid, CapabilityEntry> entries = [];
    private readonly object writeGate = new();

    /// <summary>Registers a capability under its stable identifier.</summary>
    /// <param name="entry">The normalized capability registration.</param>
    public void Register(CapabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (this.writeGate)
        {
            RegisterOne(Volatile.Read(ref this.entries), entry);
        }
    }

    /// <inheritdoc />
    public void RegisterBatch(IReadOnlyList<CapabilityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var prepared = new CapabilityEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            prepared[index] = entries[index]
                ?? throw new ArgumentException("Capability batches cannot contain null.", nameof(entries));
        }

        lock (this.writeGate)
        {
            ConcurrentDictionary<Guid, CapabilityEntry> current = Volatile.Read(ref this.entries);
            var preparedIds = new HashSet<Guid>();
            for (var index = 0; index < prepared.Length; index++)
            {
                CapabilityEntry entry = prepared[index];
                if (!preparedIds.Add(entry.CapabilityId)
                    || current.ContainsKey(entry.CapabilityId))
                {
                    throw Duplicate(entry.CapabilityId);
                }
            }

            var replacement = new ConcurrentDictionary<Guid, CapabilityEntry>(current);
            for (var index = 0; index < prepared.Length; index++)
            {
                RegisterOne(replacement, prepared[index]);
            }

            Volatile.Write(ref this.entries, replacement);
        }
    }

    /// <summary>Finds a registered capability by its stable identifier.</summary>
    /// <param name="capabilityId">The stable capability identifier.</param>
    /// <returns>The matching registration, or <see langword="null"/>.</returns>
    public CapabilityEntry? Find(Guid capabilityId)
    {
        ConcurrentDictionary<Guid, CapabilityEntry> snapshot = Volatile.Read(ref this.entries);
        return snapshot.TryGetValue(capabilityId, out CapabilityEntry? entry) ? entry : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<CapabilityEntry> Snapshot()
    {
        ConcurrentDictionary<Guid, CapabilityEntry> snapshot = Volatile.Read(ref this.entries);
        return Array.AsReadOnly(snapshot.Values.OrderBy(entry => entry.CapabilityId).ToArray());
    }

    private static void RegisterOne(
        ConcurrentDictionary<Guid, CapabilityEntry> destination,
        CapabilityEntry entry)
    {
        if (!destination.TryAdd(entry.CapabilityId, entry))
        {
            throw Duplicate(entry.CapabilityId);
        }
    }

    private static InvalidOperationException Duplicate(Guid capabilityId)
    {
        return new InvalidOperationException(
            $"Capability '{capabilityId}' is already registered.");
    }
}
