using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Gallery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Gallery;

public sealed class GalleryCuratorServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-curator-{Guid.NewGuid():N}");
    private readonly MemoryIndex index = new();
    private readonly IVisionClient vision = Substitute.For<IVisionClient>();
    private readonly IEmbeddingClient embeddings = Substitute.For<IEmbeddingClient>();

    public GalleryCuratorServiceTests()
    {
        Directory.CreateDirectory(this.root);
        this.vision.DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("""{"caption":"She reads on the sofa.","tags":["sofa","reading"]}""");
        this.embeddings.ModelId.Returns("bge-m3");
        this.embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(0).Select(_ => new float[] { 1f }).ToList());
    }

    public void Dispose()
    {
        Directory.Delete(this.root, recursive: true);
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null);

    private GalleryCuratorService Service(bool enabled = true, int cap = 40) => new(
        this.index, this.vision, this.embeddings,
        Options.Create(new GalleryCuratorOptions { Enabled = enabled, Directory = this.root, MaxCaptionsPerPass = cap, CanonicalFileName = "anchor.png" }),
        TimeProvider.System, NullLogger<GalleryCuratorService>.Instance);

    private async Task SeedAsync(params string[] names)
    {
        foreach (var name in names)
        {
            await File.WriteAllBytesAsync(Path.Combine(this.root, name), [1, 2, 3]);
        }
    }

    [Fact]
    public async Task A_Pass_Should_Index_Caption_And_Embed_Every_New_Picture()
    {
        await this.SeedAsync("a.png", "b.jpg", "notes.txt");

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal("2 indexed, 2 captioned, 2 embedded", result.Note);
        Assert.All(this.index.Entries.Values, entry => Assert.Equal("She reads on the sofa.", entry.Caption));
        Assert.Equal(2, this.index.Vectors.Count);
    }

    [Fact]
    public async Task A_Second_Pass_Should_Do_Nothing_To_Pictures_Already_Seen()
    {
        await this.SeedAsync("a.png");
        var service = this.Service();
        await service.RunPassAsync(Context(), CancellationToken.None);

        var again = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, again.Status);
        Assert.Equal(string.Empty, again.Note);
        await this.vision.Received(1).DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Captioning_Should_Stop_At_The_Cap_And_Resume_Next_Pass()
    {
        await this.SeedAsync("a.png", "b.png", "c.png");

        var first = await this.Service(cap: 2).RunPassAsync(Context(), CancellationToken.None);
        var second = await this.Service(cap: 2).RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal("3 indexed, 2 captioned, 2 embedded", first.Note);
        Assert.Equal("0 indexed, 1 captioned, 1 embedded", second.Note);
    }

    [Fact]
    public async Task A_Pass_Should_Embed_Everything_It_Captioned_Not_One_Batch()
    {
        // 2026-09-05: two passes captioned 73 pictures and embedded 64; the nine left
        // over waited eight hours for nothing. Batches are a courtesy to the embedder,
        // not a cap on the pass.
        await this.SeedAsync(Enumerable.Range(0, 40).Select(i => $"p{i:D2}.png").ToArray());

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal("40 indexed, 40 captioned, 40 embedded", result.Note);
        await this.embeddings.Received(2).EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_Anchor_Should_Be_Marked_Canonical_When_Indexed()
    {
        await this.SeedAsync("anchor.png");

        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.True(this.index.Entries["anchor.png"].IsCanonical);
    }

    [Fact]
    public async Task Disabled_Or_Missing_Folder_Should_Stay_Quiet()
    {
        await this.SeedAsync("a.png");

        var disabled = await this.Service(enabled: false).RunPassAsync(Context(), CancellationToken.None);
        Directory.Delete(this.root, recursive: true);
        var missing = await this.Service().RunPassAsync(Context(), CancellationToken.None);
        Directory.CreateDirectory(this.root);

        Assert.Empty(this.index.Entries);
        Assert.Equal((string.Empty, string.Empty), (disabled.Note, missing.Note));
    }

    /// <summary>The index in memory, enough for the curator's contract.</summary>
    private sealed class MemoryIndex : IGalleryIndex
    {
        public Dictionary<string, GalleryEntry> Entries { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string, string), float[]> Vectors { get; } = [];

        public Task UpsertAsync(GalleryEntry entry, CancellationToken cancellationToken)
        {
            this.Entries[entry.FileName] = this.Entries.TryGetValue(entry.FileName, out var existing)
                ? entry with { Caption = existing.Caption, Tags = existing.Tags, CaptionedAt = existing.CaptionedAt }
                : entry;
            return Task.CompletedTask;
        }

        public Task<GalleryEntry?> FindAsync(string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(this.Entries.GetValueOrDefault(fileName));

        public IAsyncEnumerable<GalleryEntry> ListAsync(int limit, CancellationToken cancellationToken) =>
            this.Entries.Values.OrderByDescending(e => e.CreatedAt).Take(limit).ToAsyncEnumerable();

        public IAsyncEnumerable<GalleryEntry> UncaptionedAsync(int limit, CancellationToken cancellationToken) =>
            this.Entries.Values.Where(e => e.Caption is null).OrderBy(e => e.FileName, StringComparer.Ordinal).Take(limit).ToAsyncEnumerable();

        public Task DescribeAsync(string fileName, GalleryDescription description, CancellationToken cancellationToken)
        {
            this.Entries[fileName] = this.Entries[fileName] with
            {
                Caption = description.Caption, Tags = description.Tags, CaptionModel = description.CaptionModel, CaptionedAt = description.At,
            };
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<GalleryEntry> UnembeddedAsync(string embeddingModel, int limit, CancellationToken cancellationToken) =>
            this.Entries.Values.Where(e => e.Caption is not null && !this.Vectors.ContainsKey((e.FileName, embeddingModel)))
                .Take(limit).ToAsyncEnumerable();

        public Task StoreEmbeddingAsync(string fileName, string embeddingModel, float[] embedding, CancellationToken cancellationToken)
        {
            this.Vectors[(fileName, embeddingModel)] = embedding;
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestToAsync(
            string fileName, string embeddingModel, int limit, CancellationToken cancellationToken) =>
            AsyncEnumerable.Empty<(GalleryEntry, double)>();

        public IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestAsync(
            float[] queryEmbedding, string embeddingModel, int limit, CancellationToken cancellationToken) =>
            AsyncEnumerable.Empty<(GalleryEntry, double)>();
    }
}
