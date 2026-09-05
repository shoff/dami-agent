using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Gallery;

/// <summary>One Gallery search hit.</summary>
public sealed record GalleryHit(GalleryEntry Entry, double Score);

/// <summary>Finds pictures by what is in them (ADR-0031).</summary>
public interface IGallerySearch
{
    /// <summary>The best matches for a phrase, best first.</summary>
    Task<IReadOnlyList<GalleryHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Embed the phrase, take the nearest captions, let the cross-encoder order them. The
/// same retrieval shape as memory, over pictures. Loopback only.
/// </summary>
public sealed class GallerySearch : IGallerySearch
{
    private const int CANDIDATES_PER_RESULT = 3;

    private readonly IGalleryIndex index;
    private readonly IEmbeddingClient embeddings;
    private readonly IRerankClient reranker;
    private readonly ILogger<GallerySearch> logger;

    /// <summary>Creates the search.</summary>
    public GallerySearch(
        IGalleryIndex index, IEmbeddingClient embeddings, IRerankClient reranker, ILogger<GallerySearch> logger)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentNullException.ThrowIfNull(logger);
        this.index = index;
        this.embeddings = embeddings;
        this.reranker = reranker;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GalleryHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);

        var vector = (await this.embeddings.EmbedAsync([query.Trim()], cancellationToken).ConfigureAwait(false))[0];
        var candidates = new List<GalleryHit>();
        await foreach (var (entry, distance) in this.index
            .NearestAsync(vector, this.embeddings.ModelId, limit * CANDIDATES_PER_RESULT, cancellationToken)
            .ConfigureAwait(false))
        {
            candidates.Add(new GalleryHit(entry, 1 - distance));
        }

        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var ordered = await this.RerankAsync(query, candidates, cancellationToken).ConfigureAwait(false);
        return ordered.Take(limit).ToList();
    }

    /// <summary>What the reranker reads for a picture: its caption, tags, and prompt.</summary>
    public static string Passage(GalleryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var parts = new List<string> { entry.Caption ?? string.Empty };
        if (entry.Tags is { Count: > 0 })
        {
            parts.Add(string.Join(", ", entry.Tags));
        }

        if (entry.Prompt.Length > 0)
        {
            parts.Add(entry.Prompt);
        }

        return string.Join(" — ", parts.Where(part => part.Length > 0));
    }

    private async Task<IReadOnlyList<GalleryHit>> RerankAsync(
        string query, List<GalleryHit> candidates, CancellationToken cancellationToken)
    {
        try
        {
            var order = await this.reranker
                .RankAsync(query, candidates.Select(hit => Passage(hit.Entry)).ToList(), cancellationToken)
                .ConfigureAwait(false);
            return order.Select(position => candidates[position]).ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Cosine order is a worse answer than the cross-encoder's, not a wrong one.
            this.logger.LogWarning(exception, "Gallery reranker unavailable; keeping embedding order");
            return candidates;
        }
    }
}
