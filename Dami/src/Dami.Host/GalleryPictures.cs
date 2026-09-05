using Dami.Contracts.Gallery;
using Dami.Contracts.Models;

namespace Dami.Host;

/// <summary>The Gallery's bytes behind the runtime's picture seam.</summary>
public sealed class GalleryPictures : IGalleryPictures
{
    private readonly ImageGallery gallery;

    /// <summary>Creates the loader.</summary>
    public GalleryPictures(ImageGallery gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        this.gallery = gallery;
    }

    /// <inheritdoc />
    public async Task<GeneratedImage?> LoadAsync(string fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (this.gallery.Resolve(fileName) is not { } path)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png",
        };
        return new GeneratedImage(fileName, bytes, contentType, string.Empty);
    }
}
