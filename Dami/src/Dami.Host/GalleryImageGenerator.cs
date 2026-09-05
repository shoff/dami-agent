using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Microsoft.Extensions.Options;

namespace Dami.Host;

/// <summary>Creates a new Dami portrait from one approved identity anchor.</summary>
public sealed class GalleryImageGenerator
{
    private readonly IImageGenerator provider;
    private readonly ImageGallery gallery;
    private readonly ImageGalleryOptions options;

    /// <summary>Creates the generator.</summary>
    public GalleryImageGenerator(
        IImageGenerator provider, ImageGallery gallery, IOptions<ImageGalleryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(gallery);
        ArgumentNullException.ThrowIfNull(options);
        this.provider = provider;
        this.gallery = gallery;
        this.options = options.Value;
    }

    /// <summary>Generates and persists one portrait.</summary>
    public async Task<GalleryImage> GenerateAsync(
        string scene, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        var reference = await this.ReferenceAsync(cancellationToken).ConfigureAwait(false);
        var request = new ImageRequest(
            $"{PortraitIdentity.PROMPT}\nScene and emotional beat: {scene}", "Dami gallery portrait",
            PrivacyClass.Egressable, Guid.NewGuid(), ExecutionOrigin.UserTurn)
        {
            Size = "1024x1536",
            Quality = "medium",
            Reference = reference,
        };
        var generated = await this.provider.GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await this.gallery.SaveAsync(
            generated.Bytes.ToArray(), scene, "gpt-image-2", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The anchor: the configured file, or the Gallery's own copy of it. The configured
    /// path pointed into ~/Downloads, which got cleaned out, and every portrait then
    /// failed with "not configured" while a byte-identical copy sat in the Gallery.
    /// </summary>
    private async Task<ImageReference> ReferenceAsync(CancellationToken cancellationToken)
    {
        var configured = this.options.CanonicalReferencePath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("The canonical Dami identity image is not configured.");
        }

        var path = File.Exists(configured)
            ? configured
            : this.gallery.Resolve(Path.GetFileName(configured))
                ?? throw new FileNotFoundException(
                    "The canonical Dami identity image is missing, and the Gallery has no copy.", configured);

        return new ImageReference(
            Path.GetFileName(path), "image/png",
            await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
    }
}
