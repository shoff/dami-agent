using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Gallery;

/// <summary>Makes the Gallery know what it has (migration 040).</summary>
/// <remarks>
/// Three steps a pass: every file in the folder gets an index row with its provenance;
/// pictures nobody has looked at get a caption and tags from the local vision model;
/// captioned pictures get a vector so they can be searched. Idempotent — a re-run does
/// nothing to a picture already seen — and bounded, because the vision model shares
/// the card with everything else. Nothing leaves the host.
/// </remarks>
public sealed class GalleryCuratorService : IProactiveService
{
    private const int EMBED_BATCH = 32;

    private static readonly string[] extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

    private readonly IGalleryIndex index;
    private readonly IVisionClient vision;
    private readonly IEmbeddingClient embeddings;
    private readonly GalleryCuratorOptions curatorOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<GalleryCuratorService> logger;

    /// <summary>Creates the service.</summary>
    public GalleryCuratorService(
        IGalleryIndex index,
        IVisionClient vision,
        IEmbeddingClient embeddings,
        IOptions<GalleryCuratorOptions> curatorOptions,
        TimeProvider clock,
        ILogger<GalleryCuratorService> logger)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(curatorOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.index = index;
        this.vision = vision;
        this.embeddings = embeddings;
        this.curatorOptions = curatorOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "gallery-curator";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.EightHourly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!this.curatorOptions.Enabled || !Directory.Exists(this.curatorOptions.Directory))
        {
            return ProactiveResult.quiet;
        }

        var indexed = await this.IndexAsync(cancellationToken).ConfigureAwait(false);
        var captioned = await this.CaptionAsync(cancellationToken).ConfigureAwait(false);
        var embedded = await this.EmbedAsync(cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Gallery curator: {Indexed} indexed, {Captioned} captioned, {Embedded} embedded", indexed, captioned, embedded);
        return indexed + captioned + embedded == 0
            ? ProactiveResult.quiet
            : ProactiveResult.Did($"{indexed} indexed, {captioned} captioned, {embedded} embedded");
    }

    /// <summary>Every picture in the folder has a row; new sidecars refresh provenance.</summary>
    private async Task<int> IndexAsync(CancellationToken cancellationToken)
    {
        var indexed = 0;
        foreach (var path in Directory.EnumerateFiles(this.curatorOptions.Directory)
            .Where(path => extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            var entry = GallerySidecar.Read(path, this.curatorOptions.CanonicalFileName);
            if (await this.index.FindAsync(entry.FileName, cancellationToken).ConfigureAwait(false) is null)
            {
                indexed++;
            }

            await this.index.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return indexed;
    }

    /// <summary>The local vision model looks at what nobody has looked at, up to the cap.</summary>
    private async Task<int> CaptionAsync(CancellationToken cancellationToken)
    {
        var captioned = 0;
        await foreach (var entry in this.index
            .UncaptionedAsync(this.curatorOptions.MaxCaptionsPerPass, cancellationToken).ConfigureAwait(false))
        {
            var path = Path.Combine(this.curatorOptions.Directory, entry.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var reply = await this.vision.DescribeAsync(bytes, GalleryCaption.PROMPT, cancellationToken).ConfigureAwait(false);
            var (caption, tags) = GalleryCaption.Parse(reply);
            await this.index.DescribeAsync(
                entry.FileName, new GalleryDescription(caption, tags, "qwen2.5vl", this.clock.GetUtcNow()), cancellationToken)
                .ConfigureAwait(false);
            captioned++;
        }

        return captioned;
    }

    /// <summary>Captioned pictures get a vector under the current embedder.</summary>
    private async Task<int> EmbedAsync(CancellationToken cancellationToken)
    {
        var pending = new List<GalleryEntry>();
        await foreach (var entry in this.index
            .UnembeddedAsync(this.embeddings.ModelId, EMBED_BATCH, cancellationToken).ConfigureAwait(false))
        {
            pending.Add(entry);
        }

        if (pending.Count == 0)
        {
            return 0;
        }

        var texts = pending.Select(entry => GalleryCaption.Embeddable(entry.Caption!, entry.Tags ?? [], entry.Prompt)).ToList();
        var vectors = await this.embeddings.EmbedAsync(texts, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < pending.Count; i++)
        {
            await this.index.StoreEmbeddingAsync(pending[i].FileName, this.embeddings.ModelId, vectors[i], cancellationToken)
                .ConfigureAwait(false);
        }

        return pending.Count;
    }
}
