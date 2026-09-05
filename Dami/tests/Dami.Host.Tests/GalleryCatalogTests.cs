using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
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
    private readonly IEmbeddingClient embeddings = Substitute.For<IEmbeddingClient>();

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
        this.embeddings.ModelId.Returns("bge-m3");
        return new GalleryCatalog(new ImageGallery(options, TimeProvider.System), this.index, this.search, this.embeddings);
    }

    [Fact]
    public async Task List_Should_Show_Every_File_And_Add_What_The_Index_Knows()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "seen.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(this.root, "new.png"), [1]);
        this.index.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(EntriesAsync(
            new GalleryEntry("seen.png", DateTimeOffset.UnixEpoch, GallerySource.Discord, "p", "m", false, Caption: "on a balcony", Tags: ["balcony"])));

        var cards = await this.Subject().ListAsync(false, CancellationToken.None);

        var seen = Assert.Single(cards, card => card.FileName == "seen.png");
        var fresh = Assert.Single(cards, card => card.FileName == "new.png");
        Assert.Equal(("on a balcony", "discord"), (seen.Caption, seen.Source));
        Assert.Equal((null, "unknown"), (fresh.Caption, fresh.Source));
    }

    [Fact]
    public async Task List_Should_Leave_Hidden_Pictures_Out_Unless_Asked()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "shown.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(this.root, "hidden.png"), [1]);
        this.index.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(EntriesAsync(
            new GalleryEntry("hidden.png", DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Hidden: true, Favourite: true)));

        var shown = await this.Subject().ListAsync(false, CancellationToken.None);
        var all = await this.Subject().ListAsync(true, CancellationToken.None);

        Assert.Equal(["shown.png"], shown.Select(card => card.FileName));
        Assert.Equal(2, all.Count);
        Assert.True(Assert.Single(all, card => card.FileName == "hidden.png").Favourite);
    }

    [Fact]
    public async Task Flag_Should_Index_An_Unknown_Picture_From_The_Folder_Before_Marking_It()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "new.png"), [1]);
        this.index.FindAsync("new.png", Arg.Any<CancellationToken>()).Returns((GalleryEntry?)null);

        await this.Subject().FlagAsync("new.png", favourite: true, hidden: null, CancellationToken.None);

        await this.index.Received(1).UpsertAsync(Arg.Is<GalleryEntry>(entry => entry.FileName == "new.png"), Arg.Any<CancellationToken>());
        await this.index.Received(1).SetFlagsAsync("new.png", true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Similar_Should_Ask_The_Index_Under_The_Current_Embedder_And_Score_By_Closeness()
    {
        await File.WriteAllBytesAsync(Path.Combine(this.root, "twin.png"), [1]);
        this.index.NearestToAsync("source.png", "bge-m3", 5, Arg.Any<CancellationToken>()).Returns(HitsAsync(
            (new GalleryEntry("twin.png", DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Caption: "c"), 0.1),
            (new GalleryEntry("gone.png", DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Caption: "c"), 0.2)));

        var cards = await this.Subject().SimilarAsync("source.png", 5, CancellationToken.None);

        var only = Assert.Single(cards);
        Assert.Equal("twin.png", only.FileName);
        Assert.Equal(0.9, only.Score!.Value, 6);
    }

    private static async IAsyncEnumerable<(GalleryEntry, double)> HitsAsync(params (GalleryEntry, double)[] hits)
    {
        foreach (var hit in hits)
        {
            yield return hit;
        }

        await Task.CompletedTask;
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
