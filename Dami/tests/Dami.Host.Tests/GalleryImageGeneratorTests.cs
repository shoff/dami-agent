using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class GalleryImageGeneratorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-generate-{Guid.NewGuid():N}");
    private readonly IGalleryIndex index = Substitute.For<IGalleryIndex>();

    [Fact]
    public async Task Should_Generate_From_Exactly_The_Configured_Canonical_And_Save_It()
    {
        Directory.CreateDirectory(this.root);
        var canonical = Path.Combine(this.root, "canonical.png");
        await File.WriteAllBytesAsync(canonical, [4, 5, 6]);
        var options = Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
            CanonicalReferencePath = canonical,
        });
        var provider = Substitute.For<IImageGenerator>();
        provider.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("out.png", new byte[] { 7, 8 }, "image/png", "prompt"));
        var subject = new GalleryImageGenerator(
            provider, new ImageGallery(options, TimeProvider.System), this.index, options);

        var item = await subject.GenerateAsync("laughing over coffee", CancellationToken.None);

        await provider.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request =>
                request.Reference != null
                && request.Reference.FileName == "canonical.png"
                && request.Reference.Bytes.ToArray().SequenceEqual(new byte[] { 4, 5, 6 })
                && request.Quality == "medium"),
            Arg.Any<CancellationToken>());
        Assert.True(File.Exists(Path.Combine(this.root, "gallery", item.FileName)));
    }

    [Fact]
    public async Task Should_Use_The_Gallerys_Copy_When_The_Configured_Anchor_Is_Gone()
    {
        // 2026-09-04 21:02: the configured path was in ~/Downloads, which had been cleaned
        // out; every portrait failed while the Gallery held the same bytes.
        Directory.CreateDirectory(Path.Combine(this.root, "gallery"));
        await File.WriteAllBytesAsync(Path.Combine(this.root, "gallery", "canonical.png"), [4, 5, 6]);
        var options = Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
            CanonicalReferencePath = Path.Combine(this.root, "downloads", "canonical.png"),
        });
        var provider = Substitute.For<IImageGenerator>();
        provider.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("out.png", new byte[] { 7 }, "image/png", "prompt"));
        var subject = new GalleryImageGenerator(
            provider, new ImageGallery(options, TimeProvider.System), this.index, options);

        await subject.GenerateAsync("on the porch", CancellationToken.None);

        await provider.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request =>
                request.Reference != null
                && request.Reference.Bytes.ToArray().SequenceEqual(new byte[] { 4, 5, 6 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_The_Anchor_Is_Missing_When_Neither_Copy_Exists()
    {
        var options = Options.Create(new ImageGalleryOptions
        {
            Directory = Path.Combine(this.root, "gallery"),
            CanonicalReferencePath = Path.Combine(this.root, "downloads", "canonical.png"),
        });
        var provider = Substitute.For<IImageGenerator>();
        var subject = new GalleryImageGenerator(
            provider, new ImageGallery(options, TimeProvider.System), this.index, options);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => subject.GenerateAsync("on the porch", CancellationToken.None));
        await provider.DidNotReceive().GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Should_Send_The_Picture_Itself_As_The_Thing_To_Change_And_Link_The_Result_To_It()
    {
        var galleryDir = Path.Combine(this.root, "gallery");
        Directory.CreateDirectory(galleryDir);
        await File.WriteAllBytesAsync(Path.Combine(galleryDir, "source.png"), [4, 5, 6]);
        var options = Options.Create(new ImageGalleryOptions { Directory = galleryDir });
        var provider = Substitute.For<IImageGenerator>();
        provider.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("out.png", new byte[] { 7 }, "image/png", "prompt"));
        var subject = new GalleryImageGenerator(provider, new ImageGallery(options, TimeProvider.System), this.index, options);

        var item = await subject.EditAsync("source.png", "make it golden hour", CancellationToken.None);

        await provider.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request =>
                request.EditReference
                && request.Prompt == "make it golden hour"
                && request.Reference!.FileName == "source.png"
                && request.Reference.Bytes.ToArray().SequenceEqual(new byte[] { 4, 5, 6 })),
            Arg.Any<CancellationToken>());
        Assert.True(File.Exists(Path.Combine(galleryDir, item.FileName)));
        await this.index.Received(1).UpsertAsync(
            Arg.Is<GalleryEntry>(entry => entry.FileName == item.FileName && entry.DerivedFrom == "source.png"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Should_Refuse_A_Picture_The_Gallery_Lacks_Without_Generating()
    {
        var options = Options.Create(new ImageGalleryOptions { Directory = Path.Combine(this.root, "gallery") });
        var provider = Substitute.For<IImageGenerator>();
        var subject = new GalleryImageGenerator(provider, new ImageGallery(options, TimeProvider.System), this.index, options);

        await Assert.ThrowsAsync<FileNotFoundException>(() => subject.EditAsync("ghost.png", "anything", CancellationToken.None));

        await provider.DidNotReceive().GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, true);
        }
    }
}
