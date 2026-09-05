using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Core.Gallery;

namespace Dami.Host;

/// <summary>What the Gallery client sees: the folder's pictures with what the index knows about them.</summary>
public sealed record GalleryCard(
    string FileName,
    DateTimeOffset CreatedAt,
    string Prompt,
    string Model,
    bool IsCanonical,
    string? Caption,
    IReadOnlyList<string> Tags,
    string Source,
    double? Score);

/// <summary>The folder listing joined to the index, and search over the index (ADR-0031).</summary>
public sealed class GalleryCatalog
{
    private const int INDEX_WINDOW = 2000;

    private readonly ImageGallery gallery;
    private readonly IGalleryIndex index;
    private readonly IGallerySearch search;
    private readonly IEmbeddingClient embeddings;

    /// <summary>Creates the catalog.</summary>
    public GalleryCatalog(ImageGallery gallery, IGalleryIndex index, IGallerySearch search, IEmbeddingClient embeddings)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(embeddings);
        this.gallery = gallery;
        this.index = index;
        this.search = search;
        this.embeddings = embeddings;
    }

    /// <summary>The pictures most like one, by what is in them.</summary>
    public async Task<IReadOnlyList<GalleryCard>> SimilarAsync(string fileName, int limit, CancellationToken cancellationToken)
    {
        var cards = new List<GalleryCard>();
        await foreach (var (entry, distance) in this.index
            .NearestToAsync(fileName, this.embeddings.ModelId, limit, cancellationToken).ConfigureAwait(false))
        {
            if (this.gallery.Resolve(entry.FileName) is not null)
            {
                cards.Add(Card(entry, 1 - distance));
            }
        }

        return cards;
    }

    private static GalleryCard Card(GalleryEntry entry, double? score) => new(
        entry.FileName, entry.CreatedAt, entry.Prompt, entry.Model, entry.IsCanonical,
        entry.Caption, entry.Tags ?? [], entry.Source.ToString().ToLowerInvariant(), score);

    /// <summary>Every picture in the folder, newest first, enriched where the index has caught up.</summary>
    public async Task<IReadOnlyList<GalleryCard>> ListAsync(CancellationToken cancellationToken)
    {
        var known = new Dictionary<string, GalleryEntry>(StringComparer.Ordinal);
        await foreach (var entry in this.index.ListAsync(INDEX_WINDOW, cancellationToken).ConfigureAwait(false))
        {
            known[entry.FileName] = entry;
        }

        var cards = new List<GalleryCard>();
        foreach (var item in await this.gallery.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var entry = known.GetValueOrDefault(item.FileName);
            cards.Add(new GalleryCard(
                item.FileName, item.CreatedAt, item.Prompt, item.Model, item.IsCanonical,
                entry?.Caption, entry?.Tags ?? [], (entry?.Source ?? GallerySource.Unknown).ToString().ToLowerInvariant(), null));
        }

        return cards;
    }

    /// <summary>Pictures matching a phrase, best first.</summary>
    public async Task<IReadOnlyList<GalleryCard>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var hits = await this.search.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        return hits
            .Where(hit => this.gallery.Resolve(hit.Entry.FileName) is not null)
            .Select(hit => Card(hit.Entry, hit.Score))
            .ToList();
    }
}
