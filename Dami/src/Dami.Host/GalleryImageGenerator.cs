using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Microsoft.Extensions.Options;

namespace Dami.Host;

/// <summary>Creates a new Dami portrait from one approved identity anchor.</summary>
public sealed class GalleryImageGenerator
{
    private const string MODEL = "gpt-image-2";

    private readonly IImageGenerator provider;
    private readonly ImageGallery gallery;
    private readonly IGalleryIndex index;
    private readonly ImageGalleryOptions options;

    /// <summary>Creates the generator.</summary>
    public GalleryImageGenerator(
        IImageGenerator provider, ImageGallery gallery, IGalleryIndex index, IOptions<ImageGalleryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(gallery);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(options);
        this.provider = provider;
        this.gallery = gallery;
        this.index = index;
        this.options = options.Value;
    }

    /// <summary>
    /// Changes an existing picture as instructed and keeps the result as a new one, with
    /// the link back to its source in the index (ADR-0031, <c>derived_from</c>).
    /// </summary>
    public async Task<GalleryImage> EditAsync(string fileName, string instruction, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        var path = this.gallery.Resolve(fileName)
            ?? throw new FileNotFoundException("The Gallery has no such picture.", fileName);
        var request = new ImageRequest(
            instruction.Trim(), "Dami gallery edit", PrivacyClass.Egressable, Guid.NewGuid(), ExecutionOrigin.UserTurn)
        {
            Size = "1024x1536",
            Quality = "medium",
            Reference = new ImageReference(
                fileName, "image/png", await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)),
            EditReference = true,
        };
        var generated = await this.provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        var saved = await this.gallery.SaveAsync(
            generated.Bytes.ToArray(), $"edit of {fileName}: {instruction.Trim()}", MODEL, cancellationToken)
            .ConfigureAwait(false);
        await this.RecordAsync(saved, fileName, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    /// <summary>The index learns the picture now, and the curator captions it later; the link survives both.</summary>
    private Task RecordAsync(GalleryImage saved, string? derivedFrom, CancellationToken cancellationToken) =>
        this.index.UpsertAsync(
            new GalleryEntry(saved.FileName, saved.CreatedAt, GallerySource.Chat, saved.Prompt, saved.Model, false, DerivedFrom: derivedFrom),
            cancellationToken);

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
        var saved = await this.gallery.SaveAsync(generated.Bytes.ToArray(), scene, MODEL, cancellationToken)
            .ConfigureAwait(false);
        await this.RecordAsync(saved, null, cancellationToken).ConfigureAwait(false);
        return saved;
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
