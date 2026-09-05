using Dami.Contracts.Models;

namespace Dami.Host;

/// <summary>The Gallery's identity-preserving generator, behind the runtime's portrait seam.</summary>
/// <remarks>
/// The picture is saved to the Gallery first and then read back for the channel, so a
/// portrait Steve asked for on Discord is the same artifact he later sees in the Gallery
/// tab — one generation, one file, one sidecar.
/// </remarks>
public sealed class GalleryPortraits : IPortraitGenerator
{
    private readonly GalleryImageGenerator generator;
    private readonly ImageGallery gallery;

    /// <summary>Creates the adapter.</summary>
    public GalleryPortraits(GalleryImageGenerator generator, ImageGallery gallery)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(gallery);
        this.generator = generator;
        this.gallery = gallery;
    }

    /// <inheritdoc />
    public async Task<GeneratedImage> GenerateAsync(string scene, CancellationToken cancellationToken)
    {
        var saved = await this.generator.GenerateAsync(scene, cancellationToken).ConfigureAwait(false);
        var path = this.gallery.Resolve(saved.FileName)
            ?? throw new InvalidOperationException($"The Gallery did not keep {saved.FileName}.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new GeneratedImage(saved.FileName, bytes, "image/png", saved.Prompt);
    }
}
