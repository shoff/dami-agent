using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Scout;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Scout;

/// <summary>The scout: fetch through the boundary, score locally, surface sparingly.</summary>
public sealed class InterestScoutTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private const string FEED = """
        <rss version="2.0"><channel>
          <item><title>deep dive on pgvector hnsw internals</title><link>https://example.com/pg</link></item>
          <item><title>celebrity gossip roundup</title><link>https://example.com/gossip</link></item>
        </channel></rss>
        """;

    private readonly IEgressClient egressClient = Substitute.For<IEgressClient>();
    private readonly IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();
    private readonly ISurfacingQueue surfacingQueue = Substitute.For<ISurfacingQueue>();
    private readonly ISurfacingThresholdTuner thresholdTuner = Substitute.For<ISurfacingThresholdTuner>();

    [Fact]
    public async Task RunPassAsync_Should_Be_Quiet_With_No_Feeds_Configured()
    {
        var scout = this.CreateScout(feeds: [], interests: ["databases"]);

        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Fetch_Feeds_Through_The_Egress_Boundary()
    {
        this.ArrangeFeed(FEED);
        this.ArrangeSimilar();
        var scout = this.CreateScout();

        await scout.RunPassAsync(Context(), CancellationToken.None);

        await this.egressClient.Received(1).SendAsync(
            Arg.Is<EgressRequest>(request => request.Destination.Host == "feeds.example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Surface_An_Item_Matching_An_Interest()
    {
        this.ArrangeFeed(FEED);
        // interest vector matches item 0 strongly, item 1 weakly
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Vectors(
                [1f, 0f],   // the interest
                [0.9f, 0.1f],  // pgvector item - similar
                [0f, 1f])); // gossip - orthogonal

        var scout = this.CreateScout();
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task FetchAllAsync_Should_Delay_Between_Feeds_And_Still_Fetch_Them_All()
    {
        this.ArrangeFeed(FEED);
        this.ArrangeSimilar();
        var time = new FakeTimeProvider(now);
        var scout = this.CreateScout(
            feeds: ["https://a.example.com/rss", "https://b.example.com/rss"],
            interests: ["databases"],
            feedDelaySeconds: 3,
            clock: time);

        var pass = scout.RunPassAsync(Context(), CancellationToken.None);
        // The second feed is gated behind the delay: nothing more fetches until time moves.
        while (!pass.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        await pass;
        await this.egressClient.Received(2).SendAsync(
            Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAllAsync_Should_Not_Delay_Before_The_Only_Feed()
    {
        this.ArrangeFeed(FEED);
        this.ArrangeSimilar();
        // No auto-advance: a single feed must complete without any timer firing.
        var scout = this.CreateScout(
            feeds: ["https://only.example.com/rss"], interests: ["databases"], feedDelaySeconds: 30);

        await scout.RunPassAsync(Context(), CancellationToken.None);

        await this.egressClient.Received(1).SendAsync(
            Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Apply_The_Tuned_Threshold_Not_The_Static_One()
    {
        this.ArrangeFeed(FEED);
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Vectors([1f, 0f], [0.9f, 0.1f], [0f, 1f]));
        var scout = this.CreateScout();
        // after CreateScout so this override wins (later NSubstitute setup wins)
        this.thresholdTuner
            .EffectiveThresholdAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(2.0);

        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Put_The_Link_In_The_Body()
    {
        this.ArrangeFeed(FEED);
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Vectors([1f, 0f], [0.9f, 0.1f], [0f, 1f]));

        var scout = this.CreateScout();
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal("https://example.com/pg", result.Surfacings[0].Body);
    }

    [Fact]
    public async Task RunPassAsync_Should_Be_Quiet_When_Nothing_Clears_The_Threshold()
    {
        this.ArrangeFeed(FEED);
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Vectors([1f, 0f], [0f, 1f], [0f, 1f]));

        var scout = this.CreateScout();
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Cap_Items_Per_Pass()
    {
        this.ArrangeFeed(FEED);
        this.ArrangeSimilar();
        var scout = this.CreateScout(maxItems: 1);

        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Survive_A_Dead_Feed()
    {
        this.egressClient.SendAsync(Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<EgressResponse>>(_ => throw new EgressRefusedException("not allowlisted"));

        var scout = this.CreateScout();
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunPassAsync_Should_Never_Send_An_Interest_Through_Egress()
    {
        this.ArrangeFeed(FEED);
        this.ArrangeSimilar();
        var scout = this.CreateScout(interests: ["steve's private obsession"]);

        await scout.RunPassAsync(Context(), CancellationToken.None);

        await this.egressClient.DidNotReceive().SendAsync(
            Arg.Is<EgressRequest>(request =>
                request.Destination.AbsoluteUri.Contains("obsession", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    private readonly List<SurfacingReaction> reactions = [];

    [Fact]
    public async Task RunPassAsync_Should_Suppress_An_Item_Resembling_A_Bad_Reaction()
    {
        this.ArrangeFeed(FEED);
        this.reactions.Add(new SurfacingReaction("celebrity gossip roundup", "bad: never this"));
        // interest matches both items at 0.5; the bad anchor matches item 1 exactly
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Vectors(
                [0.5f, 0.5f],  // interest
                [0f, 1f],      // bad reaction anchor
                [1f, 0f],      // pgvector item
                [0f, 1f]));    // gossip item - identical to the bad anchor

        var scout = this.CreateScout(threshold: 0.6);
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Surfacings, item => item.Title.Contains("gossip"));
    }

    [Fact]
    public async Task RunPassAsync_Should_Boost_An_Item_Resembling_A_Good_Reaction()
    {
        this.ArrangeFeed(FEED);
        this.reactions.Add(new SurfacingReaction("deep dive on pgvector hnsw internals", "good: more"));
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Vectors(
                [0.6f, 0.8f],  // interest - weak-ish match to item 0
                [1f, 0f],      // good anchor
                [1f, 0f],      // pgvector item - identical to good anchor
                [0f, 1f]));    // gossip

        // interest sim to item0 = 0.6; boost 0.15 * 1.0 lifts it over a 0.7 threshold
        var scout = this.CreateScout(threshold: 0.7);
        var result = await scout.RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains(result.Surfacings, item => item.Title.Contains("pgvector"));
    }

    private void ArrangeFeed(string xml)
    {
        this.egressClient.SendAsync(Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, xml));
    }

    private void ArrangeSimilar()
    {
        this.embeddingClient.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IReadOnlyList<string>>();
                var vectors = new List<float[]>();
                for (var index = 0; index < texts.Count; index++)
                {
                    vectors.Add([1f, 0f]);
                }

                return vectors;
            });
    }

    private static async IAsyncEnumerable<SurfacingReaction> AsAsync(List<SurfacingReaction> reactions)
    {
        foreach (var reaction in reactions)
        {
            yield return reaction;
        }

        await Task.CompletedTask;
    }

    private static IReadOnlyList<float[]> Vectors(params float[][] vectors)
    {
        return vectors;
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private InterestScout CreateScout(
        IList<string>? feeds = null,
        IList<string>? interests = null,
        int maxItems = 3,
        double threshold = 0.55,
        double feedDelaySeconds = 0,
        TimeProvider? clock = null)
    {
        var options = new InterestScoutOptions
        {
            MaxItemsPerPass = maxItems,
            SurfaceThreshold = threshold,
            FeedDelaySeconds = feedDelaySeconds,
        };
        foreach (var feed in feeds ?? ["https://feeds.example.com/rss"])
        {
            options.Feeds.Add(feed);
        }

        foreach (var interest in interests ?? ["database internals and vector search"])
        {
            options.Interests.Add(interest);
        }

        this.surfacingQueue.ReactionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.reactions));

        this.thresholdTuner
            .EffectiveThresholdAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<double>(1)));

        return new InterestScout(
            this.egressClient, this.embeddingClient, this.surfacingQueue, this.thresholdTuner,
            Options.Create(options), clock ?? new FakeTimeProvider(now),
            NullLogger<InterestScout>.Instance);
    }
}
