using System.Collections.Concurrent;

namespace Dami.Capabilities;

/// <summary>Provides a source-neutral lookup surface for registered capabilities.</summary>
public sealed class CapabilityRegistry :
    ICapabilityCatalog,
    ICapabilityInventory,
    ICapabilitySourceSnapshotRegistrar,
    IRevertibleRegistrar<CapabilityEntry>
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
        CapabilityEntry[] prepared = Snapshot(entries);

        lock (this.writeGate)
        {
            ConcurrentDictionary<Guid, CapabilityEntry> current = Volatile.Read(ref this.entries);
            ValidateNewEntries(prepared, current);

            var replacement = new ConcurrentDictionary<Guid, CapabilityEntry>(current);
            for (var index = 0; index < prepared.Length; index++)
            {
                RegisterOne(replacement, prepared[index]);
            }

            Volatile.Write(ref this.entries, replacement);
        }
    }

    /// <inheritdoc />
    public void ReplaceSourceSnapshot(
        CapabilitySource source,
        IReadOnlyList<CapabilityEntry> entries)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ArgumentNullException.ThrowIfNull(entries);
        CapabilityEntry[] prepared = Snapshot(entries);
        ValidateSource(prepared, source);
        lock (this.writeGate)
        {
            ConcurrentDictionary<Guid, CapabilityEntry> current = Volatile.Read(ref this.entries);
            ConcurrentDictionary<Guid, CapabilityEntry> replacement = WithoutSource(current, source);
            ValidateNewEntries(prepared, replacement);
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

    /// <inheritdoc />
    public bool TryRemoveExact(CapabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (this.writeGate)
        {
            ConcurrentDictionary<Guid, CapabilityEntry> current = Volatile.Read(ref this.entries);
            if (!current.TryGetValue(entry.CapabilityId, out CapabilityEntry? registered)
                || !ReferenceEquals(registered, entry))
            {
                return false;
            }

            var replacement = new ConcurrentDictionary<Guid, CapabilityEntry>(current);
            bool removed = replacement.TryRemove(entry.CapabilityId, out _);
            Volatile.Write(ref this.entries, replacement);
            return removed;
        }
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

    private static CapabilityEntry[] Snapshot(IReadOnlyList<CapabilityEntry> entries)
    {
        var snapshot = new CapabilityEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            snapshot[index] = entries[index]
                ?? throw new ArgumentException("Capability batches cannot contain null.", nameof(entries));
        }

        return snapshot;
    }

    private static void ValidateNewEntries(
        IReadOnlyList<CapabilityEntry> entries,
        IReadOnlyDictionary<Guid, CapabilityEntry> existing)
    {
        var preparedIds = new HashSet<Guid>();
        for (var index = 0; index < entries.Count; index++)
        {
            Guid capabilityId = entries[index].CapabilityId;
            if (!preparedIds.Add(capabilityId) || existing.ContainsKey(capabilityId))
            {
                throw Duplicate(capabilityId);
            }
        }
    }

    private static void ValidateSource(
        IReadOnlyList<CapabilityEntry> entries,
        CapabilitySource source)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Source != source)
            {
                throw new ArgumentException(
                    "A source snapshot can contain entries from only its declared source.",
                    nameof(entries));
            }
        }
    }

    private static ConcurrentDictionary<Guid, CapabilityEntry> WithoutSource(
        ConcurrentDictionary<Guid, CapabilityEntry> current,
        CapabilitySource source)
    {
        var replacement = new ConcurrentDictionary<Guid, CapabilityEntry>();
        foreach (KeyValuePair<Guid, CapabilityEntry> pair in current)
        {
            if (pair.Value.Source != source)
            {
                RegisterOne(replacement, pair.Value);
            }
        }

        return replacement;
    }

    private static InvalidOperationException Duplicate(Guid capabilityId)
    {
        return new InvalidOperationException(
            $"Capability '{capabilityId}' is already registered.");
    }
}
