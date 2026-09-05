using System.Text.Json;
using System.Text.RegularExpressions;
using Dami.Contracts.Gallery;

namespace Dami.Proactive.Gallery;

/// <summary>Reads a Gallery file's provenance from its sidecar and its name. Pure over the file system.</summary>
/// <remarks>
/// The Host writes <c>{FileName, CreatedAt, Prompt, Model, IsCanonical}</c> beside each
/// picture it makes; the daily-portrait pass writes the same shape. Imported pictures
/// have none, and their name is all there is to go on.
/// </remarks>
public static partial class GallerySidecar
{
    private static readonly JsonSerializerOptions lenient = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The entry for one picture, from its sidecar where present, else its name and mtime.</summary>
    public static GalleryEntry Read(string path, string canonicalFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(canonicalFileName);
        var fileName = Path.GetFileName(path);
        var canonical = string.Equals(fileName, canonicalFileName, StringComparison.Ordinal);
        var sidecar = Path.ChangeExtension(path, ".json");
        var meta = File.Exists(sidecar) ? Parse(File.ReadAllText(sidecar)) : null;
        var createdAt = meta?.CreatedAt ?? new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero);
        return new GalleryEntry(
            fileName, createdAt, SourceOf(fileName, meta), meta?.Prompt ?? string.Empty,
            meta?.Model ?? string.Empty, canonical || meta?.IsCanonical == true);
    }

    /// <summary>Where a file came from, by the naming conventions the writers use.</summary>
    public static GallerySource SourceOf(string fileName, SidecarMetadata? meta)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (ProactiveSlot().IsMatch(fileName))
        {
            return GallerySource.Proactive;
        }

        if (Generated().IsMatch(fileName))
        {
            return meta is null ? GallerySource.Unknown : GallerySource.Chat;
        }

        return meta is null ? GallerySource.Imported : GallerySource.Unknown;
    }

    [GeneratedRegex(@"^dami-\d{4}-\d{2}-\d{2}-(?:morning|midday|evening)\.png$")]
    private static partial Regex ProactiveSlot();

    [GeneratedRegex(@"^dami-\d{8}-\d{6}-[0-9a-f]{32}\.png$")]
    private static partial Regex Generated();

    private static SidecarMetadata? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SidecarMetadata>(json, lenient);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The sidecar's shape.</summary>
    public sealed record SidecarMetadata(
        string? FileName, DateTimeOffset? CreatedAt, string? Prompt, string? Model, bool? IsCanonical);
}
