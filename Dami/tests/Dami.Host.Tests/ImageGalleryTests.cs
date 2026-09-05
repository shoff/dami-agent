using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Host.Tests;

public sealed class ImageGalleryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-gallery-{Guid.NewGuid():N}");

    [Fact]
    public async Task Should_Import_Seed_Images_Without_Moving_The_Originals()
    {
        var source = Path.Combine(this.root, "source.png");
        Directory.CreateDirectory(this.root);
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var gallery = new ImageGallery(Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
            SeedFiles = [source],
        }), TimeProvider.System);

        var items = await gallery.ListAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.True(File.Exists(source));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(source));
        Assert.True(File.Exists(Path.Combine(this.root, "gallery", "source.png")));
    }

    [Fact]
    public async Task Should_Recover_The_Generated_Timestamp_From_Imported_Filenames()
    {
        var source = Path.Combine(
            this.root, "openai_gpt-image-2-medium_20260824_125147_bad555f6.png");
        Directory.CreateDirectory(this.root);
        await File.WriteAllBytesAsync(source, [1]);
        var gallery = new ImageGallery(Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
            SeedFiles = [source],
        }), TimeProvider.System);

        var item = Assert.Single(await gallery.ListAsync(CancellationToken.None));

        Assert.Equal(new DateTimeOffset(2026, 8, 24, 12, 51, 47, TimeSpan.Zero), item.CreatedAt);
    }

    [Fact]
    public async Task Should_Reload_Generated_Provenance_From_The_Sidecar()
    {
        var options = Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
        });
        var first = new ImageGallery(options, TimeProvider.System);
        await first.SaveAsync([9], "midnight snack", "gpt-image-2", CancellationToken.None);

        var restarted = new ImageGallery(options, TimeProvider.System);
        var item = Assert.Single(await restarted.ListAsync(CancellationToken.None));

        Assert.Equal("midnight snack", item.Prompt);
        Assert.Equal("gpt-image-2", item.Model);
    }

    [Fact]
    public async Task Should_Import_Multiple_Uploads_Byte_For_Byte()
    {
        var options = Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
        });
        var gallery = new ImageGallery(options, TimeProvider.System);

        var imported = await gallery.ImportAsync(
            [
                new GalleryImport("one.png", "image/png", [1, 2]),
                new GalleryImport("two.jpg", "image/jpeg", [3, 4]),
            ],
            CancellationToken.None);

        Assert.Equal(2, imported.Count);
        Assert.Equal([1, 2], await File.ReadAllBytesAsync(
            Path.Combine(this.root, "gallery", "one.png")));
        Assert.Equal([3, 4], await File.ReadAllBytesAsync(
            Path.Combine(this.root, "gallery", "two.jpg")));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, true);
        }
    }
}
