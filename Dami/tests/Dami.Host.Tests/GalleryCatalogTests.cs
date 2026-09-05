using Dami.Contracts.Gallery;
using Dami.Core.Gallery;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class GalleryCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-catalog-{Guid.NewGuid():N}");
    private readonly IGalleryIndex index = Substitute.For<IGalleryIndex>();
    private readonly IGallerySearch search = Substitute.For<IGallerySearch>();

    public GalleryCatalogTests()
    {
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        Directory.Delete(this.root, recursive: true);
    }

    private static async IAsyncEnumerable<GalleryEntry> EntriesAsync(params GalleryEntry[] entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
        }

        await Task.CompletedTask;
    }

    private GalleryCatalog Subject()
    {
        var options = Options.Create(new ImageGalleryOptions { Directory = this.root });
        return new GalleryCatalog(new ImageGallery(options, TimeProvider.System), this.index, this.search);
    }

    [Fact]
    public async Task List_Should_Show_Every_File_And_Add_What_The_Index_Knows()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "seen.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(this.root, "new.png"), [1]);
        this.index.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(EntriesAsync(
            new GalleryEntry("seen.png", DateTimeOffset.UnixEpoch, GallerySource.Discord, "p", "m", false, Caption: "on a balcony", Tags: ["balcony"])));

        var cards = await this.Subject().ListAsync(CancellationToken.None);

        var seen = Assert.Single(cards, card => card.FileName == "seen.png");
        var fresh = Assert.Single(cards, card => card.FileName == "new.png");
        Assert.Equal(("on a balcony", "discord"), (seen.Caption, seen.Source));
        Assert.Equal((null, "unknown"), (fresh.Caption, fresh.Source));
    }

    [Fact]
    public async Task Search_Should_Drop_Hits_Whose_File_Is_Gone_From_The_Folder()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "here.png"), [1]);
        this.search.SearchAsync("balcony", 5, Arg.Any<CancellationToken>()).Returns([
            new GalleryHit(new GalleryEntry("here.png", DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Caption: "c"), 0.9),
            new GalleryHit(new GalleryEntry("gone.png", DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Caption: "c"), 0.8)]);

        var cards = await this.Subject().SearchAsync("balcony", 5, CancellationToken.None);

        var only = Assert.Single(cards);
        Assert.Equal(("here.png", 0.9), (only.FileName, only.Score));
    }
}
