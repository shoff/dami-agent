using Dami.Contracts.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class GalleryPortraitsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-discord-portrait-{Guid.NewGuid():N}");

    [Fact]
    public async Task Should_Return_The_Bytes_The_Gallery_Kept()
    {
        // One generation, one file: what Discord attaches is what the Gallery tab shows.
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
            .Returns(new GeneratedImage("out.png", new byte[] { 7, 8, 9 }, "image/png", "prompt"));
        var gallery = new ImageGallery(options, TimeProvider.System);
        var subject = new GalleryPortraits(
            new GalleryImageGenerator(provider, gallery, Substitute.For<Dami.Contracts.Gallery.IGalleryIndex>(), options), gallery);

        var image = await subject.GenerateAsync("Dami painting her toes", CancellationToken.None);

        Assert.Equal(new byte[] { 7, 8, 9 }, image.Bytes.ToArray());
        Assert.True(File.Exists(Path.Combine(this.root, "gallery", image.FileName)));
        await provider.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request =>
                request.Reference != null
                && request.Prompt.Contains("Dami painting her toes", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, true);
        }
    }
}
