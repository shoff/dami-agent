using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Core.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Context;

/// <summary>The budget discipline that motivated the project, tested.</summary>
public sealed class ContextBuilderTests
{
    private static readonly DateTimeOffset asOf = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly IObservationEmbeddingStore embeddingStore = Substitute.For<IObservationEmbeddingStore>();
    private readonly IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();
    private readonly IRerankClient rerankClient = Substitute.For<IRerankClient>();
    private readonly IConclusionLedger conclusionLedger = Substitute.For<IConclusionLedger>();
    private readonly List<Observation> nearest = [];
    private readonly List<Conclusion> beliefs = [];

    [Fact]
    public void Constructor_Should_Reject_A_Null_EmbeddingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextBuilder(
            null!, this.embeddingClient, this.rerankClient, this.conclusionLedger,
            Options.Create(new ContextOptions()), new FakeTimeProvider(asOf),
            NullLogger<ContextBuilder>.Instance));
    }

    [Fact]
    public async Task BuildAsync_Should_Include_Relevant_Memories_With_Provenance()
    {
        var observation = this.Observe("worked late on the transport codec");

        var context = await this.CreateBuilder().BuildAsync("what was he working on", CancellationToken.None);

        Assert.Equal(observation.ObservationId, context.Memories[0].SourceId);
    }

    [Fact]
    public async Task BuildAsync_Should_Include_Active_Beliefs()
    {
        this.Observe("anything");
        this.Believe("builds momentum by shipping vertical slices");

        var context = await this.CreateBuilder().BuildAsync("a question", CancellationToken.None);

        Assert.Single(context.Beliefs);
    }

    [Fact]
    public async Task BuildAsync_Should_Enforce_The_Token_Budget()
    {
        for (var index = 0; index < 20; index++)
        {
            this.Observe(new string('x', 4000));
        }

        var context = await this.CreateBuilder(maxTokens: 2500)
            .BuildAsync("a question", CancellationToken.None);

        Assert.True(
            context.EstimatedTokens <= 2500,
            $"budget exceeded: {context.EstimatedTokens} tokens");
    }

    [Fact]
    public async Task BuildAsync_Should_Prefer_Beliefs_Over_Memories_Under_Pressure()
    {
        this.Observe(new string('m', 9000));
        this.Believe("the belief that must survive");

        var context = await this.CreateBuilder(maxTokens: 300)
            .BuildAsync("a question", CancellationToken.None);

        Assert.Equal((1, 0), (context.Beliefs.Count, context.Memories.Count));
    }

    [Fact]
    public async Task BuildAsync_Should_Respect_The_Memory_Cap_Before_The_Budget()
    {
        for (var index = 0; index < 20; index++)
        {
            this.Observe($"memory {index}");
        }

        var context = await this.CreateBuilder(maxMemories: 3)
            .BuildAsync("a question", CancellationToken.None);

        Assert.Equal(3, context.Memories.Count);
    }

    [Fact]
    public async Task BuildAsync_Should_Keep_Rerank_Order()
    {
        this.Observe("first by ANN");
        this.Observe("second by ANN, first by rerank");
        var builder = this.CreateBuilder();
        // Registered after CreateBuilder so it overrides the identity-order default.
        this.rerankClient.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([1, 0]);

        var context = await builder.BuildAsync("a question", CancellationToken.None);

        Assert.Equal("second by ANN, first by rerank", context.Memories[0].Content);
    }

    [Fact]
    public async Task BuildAsync_Should_Return_Empty_On_An_Empty_Index()
    {
        var context = await this.CreateBuilder().BuildAsync("a question", CancellationToken.None);

        Assert.Empty(context.Memories);
    }

    private Observation Observe(string body, DateTimeOffset? occurredAt = null)
    {
        var observation = new Observation(Guid.NewGuid(), occurredAt ?? asOf.AddMonths(-5), "test", body);
        this.nearest.Add(observation);
        return observation;
    }

    [Fact]
    public async Task BuildAsync_Should_Reserve_Slots_For_Recent_Memories()
    {
        for (var index = 0; index < 8; index++)
        {
            this.Observe($"old crisis memory {index}");
        }

        this.Observe("what happened this week", asOf.AddDays(-2));

        var context = await this.CreateBuilder(maxMemories: 4).BuildAsync("a question", CancellationToken.None);

        Assert.Contains(context.Memories, item => item.Content == "what happened this week");
    }

    [Fact]
    public async Task BuildAsync_Should_Fall_Back_To_Relevance_With_Nothing_Recent()
    {
        for (var index = 0; index < 4; index++)
        {
            this.Observe($"old memory {index}");
        }

        var context = await this.CreateBuilder(maxMemories: 4).BuildAsync("a question", CancellationToken.None);

        Assert.Equal(4, context.Memories.Count);
    }

    private void Believe(string statement)
    {
        this.beliefs.Add(new Conclusion(
            Guid.NewGuid(), null, "steve", statement, 0.9,
            ConclusionSource.ReflectionPass, asOf));
    }

    private ContextBuilder CreateBuilder(int maxTokens = 2500, int maxMemories = 8, int recentSlots = 3)
    {
        this.embeddingClient.ModelId.Returns("test-model");
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<float[]> { new float[4] });
        this.embeddingStore.NearestAsync(
                Arg.Any<float[]>(), "test-model", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsNearestAsync(this.nearest));
        this.rerankClient.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Enumerable.Range(0, callInfo.Arg<IReadOnlyList<string>>().Count).ToList());
        this.conclusionLedger.ActiveForSubjectAsync("steve", Arg.Any<CancellationToken>())
            .Returns(AsConclusionsAsync(this.beliefs));

        return new ContextBuilder(
            this.embeddingStore, this.embeddingClient, this.rerankClient, this.conclusionLedger,
            Options.Create(new ContextOptions
            {
                MaxRetrievedTokens = maxTokens,
                MaxMemories = maxMemories,
                RecentSlots = recentSlots,
            }),
            new FakeTimeProvider(asOf), NullLogger<ContextBuilder>.Instance);
    }

    private static async IAsyncEnumerable<(Observation, double)> AsNearestAsync(List<Observation> items)
    {
        foreach (var item in items)
        {
            yield return (item, 0.3);
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Conclusion> AsConclusionsAsync(List<Conclusion> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
