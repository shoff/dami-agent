using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Host.Tests;

public sealed class GalleryPicturesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-pictures-{Guid.NewGuid():N}");

    public GalleryPicturesTests()
    {
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        Directory.Delete(this.root, recursive: true);
    }

    private GalleryPictures Subject() =>
        new(new ImageGallery(Options.Create(new ImageGalleryOptions { Directory = this.root }), TimeProvider.System));

    [Fact]
    public async Task Load_Should_Return_The_Bytes_And_A_Content_Type()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "a.jpg"), [7, 8]);

        var picture = await this.Subject().LoadAsync("a.jpg", CancellationToken.None);

        Assert.Equal(("a.jpg", "image/jpeg"), (picture!.FileName, picture.ContentType));
        Assert.Equal(new byte[] { 7, 8 }, picture.Bytes.ToArray());
    }

    [Fact]
    public async Task Load_Should_Return_Null_For_A_Name_The_Gallery_Lacks()
    {
        Assert.Null(await this.Subject().LoadAsync("ghost.png", CancellationToken.None));
    }
}
