using Dami.Contracts.Context;
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
    private readonly IConclusionEmbeddingStore conclusionEmbeddingStore =
        Substitute.For<IConclusionEmbeddingStore>();
    private readonly List<Observation> nearest = [];
    private readonly List<Conclusion> beliefs = [];
    private readonly List<Conclusion> indexedBeliefs = [];

    [Fact]
    public void Constructor_Should_Reject_A_Null_EmbeddingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextBuilder(
            null!, this.conclusionEmbeddingStore, this.embeddingClient, this.rerankClient, this.conclusionLedger,
            PassThroughPlanner(), Options.Create(new ContextOptions()),
            Options.Create(new QueryPlanOptions()), new FakeTimeProvider(asOf),
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
        this.beliefs.Add(NewConclusion(statement));
    }

    private static Conclusion NewConclusion(string statement)
    {
        return new Conclusion(
            Guid.NewGuid(), null, "steve", statement, 0.9,
            ConclusionSource.ReflectionPass, asOf);
    }

    private ContextBuilder CreateBuilder(
        int maxTokens = 2500,
        int maxMemories = 8,
        int recentSlots = 3,
        IQueryPlanner? planner = null)
    {
        this.embeddingClient.ModelId.Returns("test-model");
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<float[]>)call.Arg<IReadOnlyList<string>>()
                .Select(_ => new float[4]).ToList());
        this.embeddingStore.NearestAsync(
                Arg.Any<float[]>(), "test-model", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => this.AsNearestAsync(this.nearest));
        this.rerankClient.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Enumerable.Range(0, callInfo.Arg<IReadOnlyList<string>>().Count).ToList());
        this.conclusionLedger.ActiveForSubjectAsync("steve", Arg.Any<CancellationToken>())
            .Returns(AsConclusionsAsync(this.beliefs));
        this.conclusionEmbeddingStore.NearestAsync(
                Arg.Any<float[]>(), "test-model", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => this.AsScoredConclusionsAsync(this.indexedBeliefs));

        return new ContextBuilder(
            this.embeddingStore, this.conclusionEmbeddingStore, this.embeddingClient,
            this.rerankClient, this.conclusionLedger,
            planner ?? PassThroughPlanner(),
            Options.Create(new ContextOptions
            {
                MaxRetrievedTokens = maxTokens,
                MaxMemories = maxMemories,
                RecentSlots = recentSlots,
            }),
            // Planning off keeps these cases on the single-query path they were written for.
            Options.Create(new QueryPlanOptions { Enabled = planner is not null }),
            new FakeTimeProvider(asOf), NullLogger<ContextBuilder>.Instance);
    }

    /// <summary>A planner that changes nothing — the request, searched for as written.</summary>
    private static IQueryPlanner PassThroughPlanner()
    {
        var planner = Substitute.For<IQueryPlanner>();
        planner.PlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new QueryPlan([call.ArgAt<string>(0)], [], [])));
        return planner;
    }

    [Fact]
    public async Task BuildAsync_Should_Drop_Candidates_Beyond_The_Distance_Ceiling()
    {
        this.distance = 0.9;
        this.Observe("nearest junk, still junk");

        var context = await this.CreateBuilder().BuildAsync("a question", CancellationToken.None);

        Assert.Empty(context.Memories);
    }

    [Fact]
    public async Task BuildAsync_Should_Retrieve_Beliefs_By_Similarity_When_Indexed()
    {
        this.Observe("anything");
        this.Believe("subject-scan belief that must not appear");
        this.indexedBeliefs.Add(NewConclusion("similar belief from the index"));

        var context = await this.CreateBuilder().BuildAsync("a question", CancellationToken.None);

        Assert.Equal("similar belief from the index", context.Beliefs[0].Content);
    }

    [Fact]
    public async Task BuildAsync_Should_Gate_Beliefs_On_The_Belief_Distance_Ceiling()
    {
        this.Observe("anything");
        this.beliefDistance = 0.9;
        this.indexedBeliefs.Add(NewConclusion("nearest belief, still too far"));

        var context = await this.CreateBuilder().BuildAsync("a question", CancellationToken.None);

        Assert.Empty(context.Beliefs);
    }

    private double distance = 0.3;
    private double beliefDistance = 0.3;

    private async IAsyncEnumerable<(Conclusion, double)> AsScoredConclusionsAsync(List<Conclusion> items)
    {
        foreach (var item in items)
        {
            yield return (item, this.beliefDistance);
        }

        await Task.CompletedTask;
    }

    private async IAsyncEnumerable<(Observation, double)> AsNearestAsync(List<Observation> items)
    {
        foreach (var item in items)
        {
            yield return (item, this.distance);
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

    [Fact]
    public async Task BuildAsync_Should_Search_Once_Per_Planned_Query()
    {
        this.Observe("aortic valve replacement scheduled");

        await this.CreateBuilder(planner: Planner(["heart condition", "aortic stenosis", "surgeon questions"]))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        await this.embeddingClient.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<string>>(searches => searches.Count == 3),
            Arg.Any<CancellationToken>());
        this.embeddingStore.Received(3).NearestAsync(
            Arg.Any<float[]>(), "test-model", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_Should_Not_Repeat_An_Observation_Found_By_Two_Searches()
    {
        this.Observe("aortic valve replacement scheduled");

        var context = await this.CreateBuilder(planner: Planner(["heart condition", "aortic stenosis"]))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        // Both searches return the same stubbed row; the union must dedupe by identity,
        // or an expansion that overlaps spends the budget saying one thing twice.
        Assert.Single(context.Memories);
    }

    [Fact]
    public async Task BuildAsync_Should_Include_Facts_The_Plan_Resolved()
    {
        this.Observe("we talked about the surgery for a while");

        var context = await this.CreateBuilder(
                planner: Planner(
                    ["heart condition"], ["health"], ["Severe aortic stenosis diagnosed"], new DateOnly(2026, 1, 30)))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        var fact = Assert.Single(context.Memories, item => item.Kind == "fact");
        Assert.Contains("Severe aortic stenosis diagnosed", fact.Content, StringComparison.Ordinal);

        // Facts lead: if the budget runs out it is the prose that should go missing.
        Assert.Equal("fact", context.Memories[0].Kind);
    }

    [Fact]
    public async Task BuildAsync_Should_Carry_No_Facts_When_The_Plan_Resolved_None()
    {
        this.Observe("unrelated chatter");

        var context = await this.CreateBuilder(planner: Planner(["dinner plans"]))
            .BuildAsync("what is for dinner", CancellationToken.None);

        Assert.DoesNotContain(context.Memories, item => item.Kind == "fact");
    }

    [Fact]
    public async Task BuildAsync_Should_Skip_An_Oversized_Memory_Rather_Than_Stop_Collecting()
    {
        this.Observe(new string('x', 4000));
        this.Observe("short precise fact");

        var context = await this.CreateBuilder(maxTokens: 200).BuildAsync("anything", CancellationToken.None);

        // The regression this guards: stopping at the first item too large to fit let one
        // long conversation summary end the list while short facts behind it would have fitted.
        Assert.Single(context.Memories);
        Assert.Equal("short precise fact", context.Memories[0].Content);
    }

    private static IQueryPlanner Planner(
        IReadOnlyList<string> searches,
        IReadOnlyList<string>? domains = null,
        IReadOnlyList<string>? facts = null,
        DateOnly? asOf = null)
    {
        var resolved = (facts ?? [])
            .Select(fact => new StructuredFact(Guid.NewGuid(), fact, asOf, "diagnosis"))
            .ToList();

        var planner = Substitute.For<IQueryPlanner>();
        planner.PlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueryPlan(searches, domains ?? [], resolved)));
        return planner;
    }

    [Fact]
    public async Task BuildAsync_Should_Say_So_Rather_Than_Invent_A_Date_For_An_Undated_Fact()
    {
        this.Observe("we talked about it");

        var context = await this.CreateBuilder(
                planner: Planner(["heart condition"], ["health"], ["Aortic Valve Replacement"]))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        // 25 of 84 stored health rows carry 1970-01-01 because the column forbids null and
        // extraction had no date. Rendering that verbatim tells the frontier the procedure
        // happened in 1970.
        var fact = Assert.Single(context.Memories, item => item.Kind == "fact");
        Assert.Contains("date unknown", fact.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("1970", fact.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_Should_Drop_A_Fact_That_Restates_One_Already_Kept()
    {
        this.Observe("we talked about it");

        var context = await this.CreateBuilder(
                planner: Planner(["chest pain"], ["health"], [
                    "Chest pain described as sharp, positional, and brief",
                    "Chest pain described as sharp and positional, with a spike lasting 30-40 seconds",
                ], new DateOnly(2026, 3, 2)))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        // Domains dedupe by exact text, so one episode written twice held two of the eight
        // fact slots in the live retrieval this guards.
        var facts = context.Memories.Where(item => item.Kind == "fact").ToList();
        Assert.Single(facts);
        Assert.Contains("sharp, positional, and brief", facts[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_Should_Keep_Two_Facts_That_Merely_Share_A_Subject()
    {
        this.Observe("we talked about it");

        var context = await this.CreateBuilder(
                planner: Planner(["aortic"], ["health"], [
                    "Severe aortic stenosis",
                    "Mechanical aortic valve replacement performed",
                ], new DateOnly(2026, 3, 2)))
            .BuildAsync("what should I ask the surgeon", CancellationToken.None);

        // A diagnosis and the operation for it share a subject and are not the same fact.
        Assert.Equal(2, context.Memories.Count(item => item.Kind == "fact"));
    }
}
