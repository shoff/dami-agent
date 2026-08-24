using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Models;

namespace Dami.Capabilities;

/// <summary>Local embed → ANN → rerank → bundle-expansion capability retrieval.</summary>
public sealed class SemanticCapabilityResolver : ICapabilityResolver
{
    private readonly ICapabilityIndexSynchronizer synchronizer;
    private readonly IEmbeddingClient embeddingClient;
    private readonly ICapabilityEmbeddingStore embeddingStore;
    private readonly IRerankClient rerankClient;
    private readonly ICapabilityCatalog catalog;
    private readonly ICapabilityBundleExpander bundleExpander;
    private readonly int candidateLimit;
    private readonly int resultLimit;

    /// <summary>Creates the semantic capability resolver.</summary>
    public SemanticCapabilityResolver(
        ICapabilityIndexSynchronizer synchronizer,
        IEmbeddingClient embeddingClient,
        ICapabilityEmbeddingStore embeddingStore,
        IRerankClient rerankClient,
        ICapabilityCatalog catalog,
        ICapabilityBundleExpander bundleExpander,
        CapabilityRetrievalOptions options)
    {
        ArgumentNullException.ThrowIfNull(synchronizer);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(rerankClient);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bundleExpander);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CandidateLimit, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ResultLimit, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.ResultLimit, options.CandidateLimit);
        this.synchronizer = synchronizer;
        this.embeddingClient = embeddingClient;
        this.embeddingStore = embeddingStore;
        this.rerankClient = rerankClient;
        this.catalog = catalog;
        this.bundleExpander = bundleExpander;
        this.candidateLimit = options.CandidateLimit;
        this.resultLimit = options.ResultLimit;
    }

    /// <inheritdoc />
    /// <summary>Resolves capabilities eligible for the caller's privacy class.</summary>
    public async Task<CapabilityBundle> ResolveAsync(
        string intent,
        PrivacyClass privacy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        CapabilityPrivacyPolicy.EnsureDefined(privacy);
        await this.synchronizer.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        float[] queryVector = await this.EmbedIntentAsync(intent, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CapabilityEntry> candidates = await this
            .FindCandidatesAsync(queryVector, privacy, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Guid> selectedIds = await this
            .RerankAsync(intent, candidates, cancellationToken).ConfigureAwait(false);
        return this.bundleExpander.Expand(intent, selectedIds, privacy);
    }

    private async Task<float[]> EmbedIntentAsync(
        string intent,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<float[]> vectors = await this.embeddingClient
            .EmbedAsync([intent], cancellationToken).ConfigureAwait(false);
        if (vectors.Count != 1)
        {
            throw new InvalidDataException(
                $"Embedding client returned {vectors.Count} vectors for one capability intent.");
        }

        return vectors[0];
    }

    private async Task<IReadOnlyList<CapabilityEntry>> FindCandidatesAsync(
        float[] queryVector,
        PrivacyClass privacy,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CapabilityEntry>();
        await foreach (var (capabilityId, _) in this.embeddingStore
            .NearestAsync(
                queryVector,
                this.embeddingClient.ModelId,
                this.candidateLimit,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (this.catalog.Find(capabilityId) is { } capability
                && CapabilityPrivacyPolicy.Allows(capability, privacy))
            {
                candidates.Add(capability);
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<Guid>> RerankAsync(
        string intent,
        IReadOnlyList<CapabilityEntry> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        string[] descriptions = candidates.Select(candidate => candidate.Description).ToArray();
        IReadOnlyList<int> order = await this.rerankClient
            .RankAsync(intent, descriptions, cancellationToken).ConfigureAwait(false);
        return SelectIds(candidates, order, this.resultLimit);
    }

    private static IReadOnlyList<Guid> SelectIds(
        IReadOnlyList<CapabilityEntry> candidates,
        IReadOnlyList<int> order,
        int resultLimit)
    {
        var selectedIds = new List<Guid>(Math.Min(resultLimit, order.Count));
        var seenIndices = new HashSet<int>();
        foreach (var index in order)
        {
            if ((uint)index >= (uint)candidates.Count || !seenIndices.Add(index))
            {
                throw new InvalidDataException($"Reranker returned invalid candidate index {index}.");
            }

            selectedIds.Add(candidates[index].CapabilityId);
            if (selectedIds.Count == resultLimit)
            {
                break;
            }
        }

        return selectedIds;
    }
}
