using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Core.Gallery;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Gallery;

public sealed class GallerySearchTests
{
    private readonly IGalleryIndex index = Substitute.For<IGalleryIndex>();
    private readonly IEmbeddingClient embeddings = Substitute.For<IEmbeddingClient>();
    private readonly IRerankClient reranker = Substitute.For<IRerankClient>();

    public GallerySearchTests()
    {
        this.embeddings.ModelId.Returns("bge-m3");
        this.embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 1f }]);
    }

    private static GalleryEntry Entry(string name, string caption) =>
        new(name, DateTimeOffset.UnixEpoch, GallerySource.Chat, "p", "m", false, Caption: caption, Tags: ["t"]);

    private static async IAsyncEnumerable<(GalleryEntry, double)> HitsAsync(params (GalleryEntry, double)[] hits)
    {
        foreach (var hit in hits)
        {
            yield return hit;
        }

        await Task.CompletedTask;
    }

    private GallerySearch Subject() => new(this.index, this.embeddings, this.reranker, NullLogger<GallerySearch>.Instance);

    [Fact]
    public async Task Search_Should_Embed_The_Phrase_Take_Neighbours_And_Let_The_Reranker_Order_Them()
    {
        this.index.NearestAsync(Arg.Any<float[]>(), "bge-m3", 6, Arg.Any<CancellationToken>())
            .Returns(HitsAsync((Entry("a.png", "kitchen"), 0.2), (Entry("b.png", "balcony at dusk"), 0.3)));
        this.reranker.RankAsync("balcony", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([1, 0]);

        var hits = await this.Subject().SearchAsync("balcony", 2, CancellationToken.None);

        Assert.Equal(["b.png", "a.png"], hits.Select(hit => hit.Entry.FileName));
        await this.reranker.Received(1).RankAsync(
            "balcony", Arg.Is<IReadOnlyList<string>>(passages => passages[1].Contains("balcony at dusk", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Reranker_Failure_Should_Keep_Embedding_Order_Rather_Than_Fail()
    {
        this.index.NearestAsync(Arg.Any<float[]>(), "bge-m3", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(HitsAsync((Entry("a.png", "x"), 0.1), (Entry("b.png", "y"), 0.2)));
        this.reranker.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<int>>>(_ => throw new HttpRequestException("reranker down"));

        var hits = await this.Subject().SearchAsync("anything", 5, CancellationToken.None);

        Assert.Equal(["a.png", "b.png"], hits.Select(hit => hit.Entry.FileName));
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public async Task No_Neighbours_Means_No_Hits_And_No_Rerank_Call()
    {
        this.index.NearestAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(HitsAsync());

        var hits = await this.Subject().SearchAsync("nothing", 5, CancellationToken.None);

        Assert.Empty(hits);
        await this.reranker.DidNotReceive().RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_Passage_Reads_Caption_Then_Tags_Then_Prompt()
    {
        Assert.Equal("kitchen — t — p", GallerySearch.Passage(Entry("a.png", "kitchen")));
    }
}
