using Dami.Contracts.Models;
using Dami.Host.Discord;

namespace Dami.Host;

/// <summary>The Gallery's identity-preserving generator, offered to the Discord gateway.</summary>
/// <remarks>
/// The picture is saved to the Gallery first and then read back for the channel, so a
/// portrait Steve asked for on Discord is the same artifact he later sees in the Gallery
/// tab — one generation, one file, one sidecar.
/// </remarks>
public sealed class DiscordGalleryPortraits : IDiscordPortraitGenerator
{
    private readonly GalleryImageGenerator generator;
    private readonly ImageGallery gallery;

    /// <summary>Creates the adapter.</summary>
    public DiscordGalleryPortraits(GalleryImageGenerator generator, ImageGallery gallery)
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
