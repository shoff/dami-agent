using Dami.Contracts.Gallery;
using Dami.Proactive.Gallery;
using Xunit;

namespace Dami.Proactive.Tests.Gallery;

public sealed class GallerySidecarTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-sidecar-{Guid.NewGuid():N}");

    public GallerySidecarTests()
    {
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        Directory.Delete(this.root, recursive: true);
    }

    [Theory]
    [InlineData("dami-2026-09-05-morning.png", true, GallerySource.Proactive)]
    [InlineData("dami-20260905-144323-7ed9d5f5b46a4caa98072eaa8525899e.png", true, GallerySource.Chat)]
    [InlineData("dami-20260905-144323-7ed9d5f5b46a4caa98072eaa8525899e.png", false, GallerySource.Unknown)]
    [InlineData("openai_gpt-image-2-medium_20260726_140042_9808fb1e.png", false, GallerySource.Imported)]
    [InlineData("holiday.jpg", false, GallerySource.Imported)]
    public void Source_Follows_The_Writers_Naming_Conventions(string fileName, bool withSidecar, GallerySource expected)
    {
        var meta = withSidecar ? new GallerySidecar.SidecarMetadata(fileName, null, "p", "m", false) : null;

        Assert.Equal(expected, GallerySidecar.SourceOf(fileName, meta));
    }

    [Fact]
    public async Task A_Sidecar_Supplies_Prompt_Model_And_Time()
    {
        var path = Path.Combine(this.root, "dami-20260905-144323-7ed9d5f5b46a4caa98072eaa8525899e.png");
        await File.WriteAllBytesAsync(path, [1]);
        await File.WriteAllTextAsync(
            Path.ChangeExtension(path, ".json"),
            """{"FileName":"x","CreatedAt":"2026-09-05T14:43:23+00:00","Prompt":"on the porch","Model":"gpt-image-2","IsCanonical":false}""");

        var entry = GallerySidecar.Read(path, "anchor.png");

        Assert.Equal(("on the porch", "gpt-image-2", GallerySource.Chat, false), (entry.Prompt, entry.Model, entry.Source, entry.IsCanonical));
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 14, 43, 23, TimeSpan.Zero), entry.CreatedAt);
    }

    [Fact]
    public async Task Without_A_Sidecar_The_File_Is_Imported_And_The_Anchor_Is_Marked()
    {
        var path = Path.Combine(this.root, "anchor.png");
        await File.WriteAllBytesAsync(path, [1]);

        var entry = GallerySidecar.Read(path, "anchor.png");

        Assert.Equal((GallerySource.Imported, true, string.Empty), (entry.Source, entry.IsCanonical, entry.Prompt));
    }
}
