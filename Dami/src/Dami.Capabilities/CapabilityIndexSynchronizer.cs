using Dami.Contracts.Capabilities;
using Dami.Contracts.Models;

namespace Dami.Capabilities;

/// <summary>Synchronizes source-neutral registry descriptions into the derived vector index.</summary>
public sealed class CapabilityIndexSynchronizer : ICapabilityIndexSynchronizer
{
    private readonly ICapabilityInventory inventory;
    private readonly ICapabilityEmbeddingStore embeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly SemaphoreSlim synchronizationGate = new(1, 1);

    /// <summary>Creates the capability-index synchronizer.</summary>
    public CapabilityIndexSynchronizer(
        ICapabilityInventory inventory,
        ICapabilityEmbeddingStore embeddingStore,
        IEmbeddingClient embeddingClient)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        this.inventory = inventory;
        this.embeddingStore = embeddingStore;
        this.embeddingClient = embeddingClient;
    }

    /// <inheritdoc />
    public async Task<CapabilityIndexSyncResult> SynchronizeAsync(
        CancellationToken cancellationToken)
    {
        await this.synchronizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await this.SynchronizeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.synchronizationGate.Release();
        }
    }

    private async Task<CapabilityIndexSyncResult> SynchronizeCoreAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapabilityEntry> snapshot = this.inventory.Snapshot();
        IReadOnlyDictionary<Guid, string> indexedVersions = await this.embeddingStore
            .VersionsAsync(this.embeddingClient.ModelId, cancellationToken).ConfigureAwait(false);
        SynchronizationPlan plan = CreatePlan(snapshot, indexedVersions);
        IReadOnlyList<float[]> vectors = await this
            .EmbedAsync(plan.Changed, cancellationToken).ConfigureAwait(false);

        await this.UpsertAsync(plan.Changed, vectors, cancellationToken).ConfigureAwait(false);
        await this.RemoveAsync(plan.StaleIds, cancellationToken).ConfigureAwait(false);

        return new CapabilityIndexSyncResult(plan.Changed.Count, plan.StaleIds.Count);
    }

    private static SynchronizationPlan CreatePlan(
        IReadOnlyList<CapabilityEntry> snapshot,
        IReadOnlyDictionary<Guid, string> indexedVersions)
    {
        var changed = new List<CapabilityEntry>();
        var registeredIds = new HashSet<Guid>();
        foreach (CapabilityEntry entry in snapshot)
        {
            registeredIds.Add(entry.CapabilityId);
            if (!indexedVersions.TryGetValue(entry.CapabilityId, out string? indexedVersion)
                || !string.Equals(indexedVersion, entry.Version, StringComparison.Ordinal))
            {
                changed.Add(entry);
            }
        }

        Guid[] staleIds = indexedVersions.Keys
            .Where(capabilityId => !registeredIds.Contains(capabilityId))
            .Order()
            .ToArray();
        return new SynchronizationPlan(changed, staleIds);
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<CapabilityEntry> changed,
        CancellationToken cancellationToken)
    {
        var descriptions = new List<string>(changed.Count);
        foreach (CapabilityEntry entry in changed)
        {
            descriptions.Add(entry.Description);
        }

        IReadOnlyList<float[]> vectors = descriptions.Count == 0
            ? []
            : await this.embeddingClient.EmbedAsync(descriptions, cancellationToken).ConfigureAwait(false);
        if (vectors.Count != changed.Count)
        {
            throw new InvalidDataException(
                $"Embedding client returned {vectors.Count} vectors for {changed.Count} capability descriptions.");
        }

        return vectors;
    }

    private async Task UpsertAsync(
        IReadOnlyList<CapabilityEntry> changed,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < changed.Count; index++)
        {
            CapabilityEntry entry = changed[index];
            await this.embeddingStore.UpsertAsync(
                entry.CapabilityId,
                entry.Version,
                this.embeddingClient.ModelId,
                vectors[index],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RemoveAsync(
        IReadOnlyList<Guid> staleIds,
        CancellationToken cancellationToken)
    {
        foreach (Guid staleId in staleIds)
        {
            await this.embeddingStore.RemoveAsync(
                staleId,
                this.embeddingClient.ModelId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record SynchronizationPlan(
        IReadOnlyList<CapabilityEntry> Changed,
        IReadOnlyList<Guid> StaleIds);
}
