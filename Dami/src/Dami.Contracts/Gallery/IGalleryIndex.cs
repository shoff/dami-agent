namespace Dami.Contracts.Gallery;

/// <summary>Where a Gallery picture came from.</summary>
public enum GallerySource
{
    /// <summary>Unknown, typically a file with no sidecar and no recognisable name.</summary>
    Unknown,

    /// <summary>Made in a chat turn (GUI or Discord) or the Gallery composer.</summary>
    Chat,

    /// <summary>Made on Discord.</summary>
    Discord,

    /// <summary>Made by a scheduled job.</summary>
    Scheduled,

    /// <summary>Made by the proactive daily-portrait pass.</summary>
    Proactive,

    /// <summary>Imported by hand.</summary>
    Imported,
}

/// <summary>One indexed Gallery picture: its provenance, and what the local vision model saw.</summary>
public sealed record GalleryEntry(
    string FileName,
    DateTimeOffset CreatedAt,
    GallerySource Source,
    string Prompt,
    string Model,
    bool IsCanonical,
    string? Caption = null,
    IReadOnlyList<string>? Tags = null,
    string? CaptionModel = null,
    DateTimeOffset? CaptionedAt = null,
    string? DerivedFrom = null,
    bool Favourite = false,
    bool Hidden = false);

/// <summary>What the vision model saw in one picture.</summary>
public sealed record GalleryDescription(string Caption, IReadOnlyList<string> Tags, string CaptionModel, DateTimeOffset At);

/// <summary>The Gallery's index (migration 040): the folder is the bytes, this is the meaning.</summary>
public interface IGalleryIndex
{
    /// <summary>Adds a picture, or refreshes its provenance if it is already known. Captions are kept.</summary>
    Task UpsertAsync(GalleryEntry entry, CancellationToken cancellationToken);

    /// <summary>One entry by file name.</summary>
    Task<GalleryEntry?> FindAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>The newest first, hidden ones included; the entry says which are hidden.</summary>
    IAsyncEnumerable<GalleryEntry> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Marks a picture favourite or hidden; a null leaves that flag as it is.</summary>
    Task SetFlagsAsync(string fileName, bool? favourite, bool? hidden, CancellationToken cancellationToken);

    /// <summary>Pictures the vision model has not described yet, oldest indexed first.</summary>
    IAsyncEnumerable<GalleryEntry> UncaptionedAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Records what the vision model saw.</summary>
    Task DescribeAsync(string fileName, GalleryDescription description, CancellationToken cancellationToken);

    /// <summary>Captioned pictures without a vector under this model.</summary>
    IAsyncEnumerable<GalleryEntry> UnembeddedAsync(string embeddingModel, int limit, CancellationToken cancellationToken);

    /// <summary>Stores the caption's vector.</summary>
    Task StoreEmbeddingAsync(string fileName, string embeddingModel, float[] embedding, CancellationToken cancellationToken);

    /// <summary>The pictures most like a given one, by caption, hidden ones and itself excluded.</summary>
    IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestToAsync(
        string fileName, string embeddingModel, int limit, CancellationToken cancellationToken);

    /// <summary>The nearest pictures to a query vector, hidden ones excluded.</summary>
    IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestAsync(
        float[] queryEmbedding, string embeddingModel, int limit, CancellationToken cancellationToken);
}
