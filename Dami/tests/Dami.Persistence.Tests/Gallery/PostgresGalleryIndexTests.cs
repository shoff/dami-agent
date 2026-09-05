using Dami.Contracts.Gallery;
using Dami.Persistence.Gallery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Gallery;

/// <summary>The Gallery index against a live database (migration 040).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresGalleryIndexTests
{
    private const string MODEL = "test-model";
    private static readonly DateTimeOffset at = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresGalleryIndexTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private PostgresGalleryIndex Index() => new(
        this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    private static GalleryEntry Entry(string name, int minutesAgo = 0) =>
        new(name, at.AddMinutes(-minutesAgo), GallerySource.Chat, "on the porch", "gpt-image-2", false);

    private static float[] Vector(float x) => [.. Enumerable.Repeat(x, 1024)];

    private static async Task<List<T>> AllAsync<T>(IAsyncEnumerable<T> items)
    {
        var list = new List<T>();
        await foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    [Fact]
    public async Task Upsert_Should_Refresh_Provenance_But_Keep_A_Caption()
    {
        await this.fixture.ResetAsync();
        var index = this.Index();
        await index.UpsertAsync(Entry("a.png"), CancellationToken.None);
        await index.DescribeAsync("a.png", new GalleryDescription("she reads", ["sofa"], "qwen2.5vl", at), CancellationToken.None);

        await index.UpsertAsync(Entry("a.png") with { Prompt = "revised" }, CancellationToken.None);

        var found = await index.FindAsync("a.png", CancellationToken.None);
        Assert.Equal(("revised", "she reads"), (found!.Prompt, found.Caption));
        Assert.Equal(["sofa"], found.Tags!);
    }

    [Fact]
    public async Task Uncaptioned_Should_Exclude_Described_And_Hidden_Pictures()
    {
        await this.fixture.ResetAsync();
        var index = this.Index();
        await index.UpsertAsync(Entry("seen.png"), CancellationToken.None);
        await index.UpsertAsync(Entry("unseen.png"), CancellationToken.None);
        await index.DescribeAsync("seen.png", new GalleryDescription("c", [], "m", at), CancellationToken.None);

        var pending = await AllAsync(index.UncaptionedAsync(10, CancellationToken.None));

        Assert.Equal(["unseen.png"], pending.Select(entry => entry.FileName));
    }

    [Fact]
    public async Task Nearest_Should_Rank_By_Cosine_Distance_And_Skip_Hidden()
    {
        await this.fixture.ResetAsync();
        var index = this.Index();
        foreach (var name in new[] { "near.png", "far.png" })
        {
            await index.UpsertAsync(Entry(name), CancellationToken.None);
            await index.DescribeAsync(name, new GalleryDescription("c", [], "m", at), CancellationToken.None);
        }

        await index.StoreEmbeddingAsync("near.png", MODEL, Vector(1f), CancellationToken.None);
        await index.StoreEmbeddingAsync("far.png", MODEL, [.. Enumerable.Range(0, 1024).Select(i => i % 2 == 0 ? 1f : -1f)], CancellationToken.None);

        var ranked = await AllAsync(index.NearestAsync(Vector(1f), MODEL, 5, CancellationToken.None));

        Assert.Equal(["near.png", "far.png"], ranked.Select(hit => hit.Entry.FileName));
        Assert.True(ranked[0].Distance < ranked[1].Distance);
        Assert.Empty(await AllAsync(index.UnembeddedAsync(MODEL, 10, CancellationToken.None)));
    }

    [Fact]
    public async Task NearestTo_Should_Rank_Other_Pictures_By_Closeness_And_Never_Return_The_Picture_Itself()
    {
        await this.fixture.ResetAsync();
        var index = this.Index();
        foreach (var (name, vector) in new[] { ("me.png", Vector(1f)), ("twin.png", Vector(0.9f)), ("far.png", [.. Enumerable.Range(0, 1024).Select(i => i % 2 == 0 ? 1f : -1f)]) })
        {
            await index.UpsertAsync(Entry(name), CancellationToken.None);
            await index.DescribeAsync(name, new GalleryDescription("c", [], "m", at), CancellationToken.None);
            await index.StoreEmbeddingAsync(name, MODEL, vector, CancellationToken.None);
        }

        var ranked = await AllAsync(index.NearestToAsync("me.png", MODEL, 5, CancellationToken.None));

        Assert.Equal(["twin.png", "far.png"], ranked.Select(hit => hit.Entry.FileName));
    }

    [Fact]
    public async Task Unembedded_Should_List_Captioned_Pictures_Without_A_Vector_Under_That_Model()
    {
        await this.fixture.ResetAsync();
        var index = this.Index();
        await index.UpsertAsync(Entry("a.png"), CancellationToken.None);
        await index.UpsertAsync(Entry("b.png"), CancellationToken.None);
        await index.DescribeAsync("a.png", new GalleryDescription("c", [], "m", at), CancellationToken.None);
        await index.StoreEmbeddingAsync("a.png", "other-model", Vector(0.5f), CancellationToken.None);

        var pending = await AllAsync(index.UnembeddedAsync(MODEL, 10, CancellationToken.None));

        // b has no caption yet; a is captioned but only under another model.
        Assert.Equal(["a.png"], pending.Select(entry => entry.FileName));
    }

    [Fact]
    public async Task Describe_Should_Refuse_An_Unindexed_Picture()
    {
        await this.fixture.ResetAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => this.Index()
            .DescribeAsync("ghost.png", new GalleryDescription("c", [], "m", at), CancellationToken.None));
    }
}
