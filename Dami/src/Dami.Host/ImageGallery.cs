using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Dami.Host;

/// <summary>Owns Dami's local portrait artifacts and their small provenance sidecars.</summary>
public sealed class ImageGallery
{
    private readonly ImageGalleryOptions options;
    private readonly TimeProvider clock;

    /// <summary>Creates the gallery.</summary>
    public ImageGallery(IOptions<ImageGalleryOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        this.options = options.Value;
        this.clock = clock;
    }

    /// <summary>Imports configured seeds and returns newest-first gallery metadata.</summary>
    public async Task<IReadOnlyList<GalleryImage>> ListAsync(CancellationToken cancellationToken)
    {
        await this.ImportSeedsAsync(cancellationToken).ConfigureAwait(false);
        var files = Directory.EnumerateFiles(this.options.Directory, "*.png")
            .Concat(Directory.EnumerateFiles(this.options.Directory, "*.jpg"))
            .Concat(Directory.EnumerateFiles(this.options.Directory, "*.jpeg"))
            .Concat(Directory.EnumerateFiles(this.options.Directory, "*.webp"))
            .Concat(Directory.EnumerateFiles(this.options.Directory, "*.gif"));
        return files.Select(this.Describe)
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    /// <summary>Returns an image path only when its basename resolves inside the gallery.</summary>
    public string? Resolve(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        var path = Path.Combine(this.options.Directory, safe);
        return string.Equals(safe, fileName, StringComparison.Ordinal) && File.Exists(path)
            ? path
            : null;
    }

    /// <summary>Persists a generated image and its provenance.</summary>
    public async Task<GalleryImage> SaveAsync(
        byte[] bytes, string prompt, string model, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(this.options.Directory);
        var now = this.clock.GetUtcNow();
        var name = $"dami-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png";
        var path = Path.Combine(this.options.Directory, name);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var item = new GalleryImage(name, now, prompt, model, false);
        await this.WriteMetadataAsync(item, cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>Copies a user-selected batch into durable gallery storage.</summary>
    public async Task<IReadOnlyList<GalleryImage>> ImportAsync(
        IReadOnlyList<GalleryImport> imports, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imports);
        if (imports.Count is 0 or > 20)
        {
            throw new InvalidDataException("Import between 1 and 20 images at a time.");
        }

        Directory.CreateDirectory(this.options.Directory);
        var saved = new List<GalleryImage>(imports.Count);
        foreach (var import in imports)
        {
            saved.Add(await this.ImportOneAsync(import, cancellationToken).ConfigureAwait(false));
        }

        return saved;
    }

    private async Task<GalleryImage> ImportOneAsync(
        GalleryImport import, CancellationToken cancellationToken)
    {
        if (import.Bytes.Length is 0 or > 20 * 1024 * 1024)
        {
            throw new InvalidDataException("Gallery imports must be between 1 byte and 20 MiB.");
        }

        var name = UniqueName(Path.GetFileName(import.FileName), this.options.Directory);
        var path = Path.Combine(this.options.Directory, name);
        await File.WriteAllBytesAsync(path, import.Bytes, cancellationToken).ConfigureAwait(false);
        var item = new GalleryImage(name, this.clock.GetUtcNow(), string.Empty, "imported", false);
        await this.WriteMetadataAsync(item, cancellationToken).ConfigureAwait(false);
        return item;
    }

    private async Task ImportSeedsAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(this.options.Directory);
        foreach (var source in this.options.SeedFiles.Where(File.Exists))
        {
            var destination = Path.Combine(this.options.Directory, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                await CopyAsync(source, destination, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private GalleryImage Describe(string path)
    {
        var sidecar = Path.ChangeExtension(path, ".json");
        if (File.Exists(sidecar))
        {
            return JsonSerializer.Deserialize<GalleryImage>(File.ReadAllText(sidecar))
                ?? throw new InvalidDataException($"Invalid gallery metadata: {sidecar}");
        }

        var name = Path.GetFileName(path);
        var canonical = !string.IsNullOrWhiteSpace(this.options.CanonicalReferencePath)
            && string.Equals(
                name, Path.GetFileName(this.options.CanonicalReferencePath),
                StringComparison.Ordinal);
        return new GalleryImage(name, Timestamp(name, path), string.Empty, "gpt-image-2", canonical);
    }

    private Task WriteMetadataAsync(GalleryImage item, CancellationToken cancellationToken)
    {
        var path = Path.Combine(this.options.Directory, Path.ChangeExtension(item.FileName, ".json"));
        return File.WriteAllTextAsync(
            path, JsonSerializer.Serialize(item), cancellationToken);
    }

    private static async Task CopyAsync(
        string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static string UniqueName(string requested, string directory)
    {
        var extension = Path.GetExtension(requested).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".gif")
        {
            throw new InvalidDataException("Only image files can be imported into the gallery.");
        }

        var candidate = Path.GetFileNameWithoutExtension(requested) + extension;
        return File.Exists(Path.Combine(directory, candidate))
            ? $"{Path.GetFileNameWithoutExtension(candidate)}-{Guid.NewGuid():N}{extension}"
            : candidate;
    }

    private static DateTimeOffset Timestamp(string name, string path)
    {
        var parts = name.Split('_');
        var marker = Enumerable.Range(0, Math.Max(0, parts.Length - 1))
            .Where(index => parts[index].Length == 8 && parts[index + 1].Length == 6)
            .Select(index => parts[index] + "_" + parts[index + 1])
            .FirstOrDefault();
        return DateTimeOffset.TryParseExact(
            marker, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : File.GetCreationTimeUtc(path);
    }
}

/// <summary>Stable metadata rendered by the gallery client.</summary>
public sealed record GalleryImage(
    string FileName, DateTimeOffset CreatedAt, string Prompt, string Model, bool IsCanonical);

/// <summary>One local image selected for byte-preserving gallery import.</summary>
public sealed record GalleryImport(string FileName, string ContentType, byte[] Bytes);
