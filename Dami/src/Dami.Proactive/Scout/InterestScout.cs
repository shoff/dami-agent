using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Scout;

/// <summary>The first proactive service (D-019): feeds in, a few good items out.</summary>
/// <remarks>
/// Chosen first for its feedback loop, not its value — Steve knows within thirty seconds
/// whether a recommendation was good, and that reaction trains the taste model every
/// later service depends on. It is also the safest: the worst case is a bad suggestion.
///
/// The privacy shape (D-012) is visible in the dependencies: interests are embedded
/// through the local <see cref="IEmbeddingClient"/> and never leave; only bare feed
/// fetches cross the <see cref="IEgressClient"/> boundary, carrying nothing derived
/// from the profile.
/// </remarks>
public sealed class InterestScout : IProactiveService
{
    private readonly IEgressClient egressClient;
    private readonly IEmbeddingClient embeddingClient;
    private readonly ISurfacingQueue surfacingQueue;
    private readonly InterestScoutOptions scoutOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<InterestScout> logger;

    /// <summary>Creates the scout.</summary>
    public InterestScout(
        IEgressClient egressClient,
        IEmbeddingClient embeddingClient,
        ISurfacingQueue surfacingQueue,
        IOptions<InterestScoutOptions> scoutOptions,
        TimeProvider clock,
        ILogger<InterestScout> logger)
    {
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(surfacingQueue);
        ArgumentNullException.ThrowIfNull(scoutOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.egressClient = egressClient;
        this.embeddingClient = embeddingClient;
        this.surfacingQueue = surfacingQueue;
        this.scoutOptions = scoutOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "interest-scout";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (this.scoutOptions.Interests.Count == 0 || this.scoutOptions.Feeds.Count == 0)
        {
            this.logger.LogWarning("Interest scout has no feeds or no interests configured; pass is quiet");
            return ProactiveResult.quiet;
        }

        var items = await this.FetchAllAsync(context, cancellationToken).ConfigureAwait(false);
        var fresh = KeepFresh(items, context.LastRanAt);

        if (fresh.Count == 0)
        {
            return ProactiveResult.quiet;
        }

        var reactions = await this.LoadReactionsAsync(cancellationToken).ConfigureAwait(false);
        var scored = await this.ScoreAsync(fresh, reactions, cancellationToken).ConfigureAwait(false);
        return this.BuildResult(scored);
    }

    private async Task<List<FeedItem>> FetchAllAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        var items = new List<FeedItem>();

        foreach (var feed in this.scoutOptions.Feeds)
        {
            try
            {
                var request = new EgressRequest(
                    new Uri(feed), "interest scout feed scan", context.TraceId,
                    ExecutionOrigin.ScheduledService);
                var response = await this.egressClient
                    .SendAsync(request, cancellationToken).ConfigureAwait(false);
                items.AddRange(FeedParser.Parse(response.Body));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One dead feed must not silence the rest of the pass.
                this.logger.LogWarning(exception, "Feed {Feed} failed; continuing", feed);
            }
        }

        return items;
    }

    private static List<FeedItem> KeepFresh(List<FeedItem> items, DateTimeOffset? lastRanAt)
    {
        if (lastRanAt is null)
        {
            return items;
        }

        var fresh = new List<FeedItem>();
        foreach (var item in items)
        {
            // An undated item is kept: dropping it silently would hide whole feeds that
            // omit dates, and the similarity threshold still gates it.
            if (item.PublishedAt is null || item.PublishedAt > lastRanAt)
            {
                fresh.Add(item);
            }
        }

        return fresh;
    }

    private async Task<List<SurfacingReaction>> LoadReactionsAsync(CancellationToken cancellationToken)
    {
        var reactions = new List<SurfacingReaction>();
        await foreach (var reaction in this.surfacingQueue
            .ReactionsAsync(this.scoutOptions.MaxReactions, cancellationToken).ConfigureAwait(false))
        {
            if (reaction.IsPositive || reaction.IsNegative)
            {
                reactions.Add(reaction);
            }
        }

        return reactions;
    }

    private async Task<List<(FeedItem Item, double Score)>> ScoreAsync(
        List<FeedItem> items,
        List<SurfacingReaction> reactions,
        CancellationToken cancellationToken)
    {
        var texts = new List<string>(this.scoutOptions.Interests);
        foreach (var reaction in reactions)
        {
            texts.Add(reaction.Title);
        }

        foreach (var item in items)
        {
            texts.Add(item.Title);
        }

        var vectors = await this.embeddingClient.EmbedAsync(texts, cancellationToken).ConfigureAwait(false);
        var interestCount = this.scoutOptions.Interests.Count;
        var itemsStart = interestCount + reactions.Count;

        var scored = new List<(FeedItem, double)>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var itemVector = vectors[itemsStart + index];
            var score = BestSimilarity(vectors, 0, interestCount, itemVector)
                + this.FeedbackAdjustment(vectors, reactions, interestCount, itemVector);
            scored.Add((items[index], score));
        }

        return scored;
    }

    /// <summary>The taste model learning: resemblance to rated items moves the score.</summary>
    private double FeedbackAdjustment(
        IReadOnlyList<float[]> vectors,
        List<SurfacingReaction> reactions,
        int reactionsStart,
        float[] itemVector)
    {
        var bestGood = 0.0;
        var bestBad = 0.0;

        for (var index = 0; index < reactions.Count; index++)
        {
            var similarity = Cosine(vectors[reactionsStart + index], itemVector);
            if (reactions[index].IsPositive && similarity > bestGood)
            {
                bestGood = similarity;
            }
            else if (reactions[index].IsNegative && similarity > bestBad)
            {
                bestBad = similarity;
            }
        }

        return (bestGood * this.scoutOptions.FeedbackBoost)
            - (bestBad * this.scoutOptions.FeedbackPenalty);
    }

    private static double BestSimilarity(
        IReadOnlyList<float[]> vectors,
        int start,
        int count,
        float[] itemVector)
    {
        var best = 0.0;
        for (var index = start; index < start + count; index++)
        {
            var similarity = Cosine(vectors[index], itemVector);
            if (similarity > best)
            {
                best = similarity;
            }
        }

        return best;
    }

    private ProactiveResult BuildResult(List<(FeedItem Item, double Score)> scored)
    {
        scored.Sort((left, right) => right.Score.CompareTo(left.Score));

        var surfacings = new List<Surfacing>();
        var now = this.clock.GetUtcNow();

        foreach (var (item, score) in scored)
        {
            if (score < this.scoutOptions.SurfaceThreshold
                || surfacings.Count >= this.scoutOptions.MaxItemsPerPass)
            {
                break;
            }

            surfacings.Add(new Surfacing(
                Guid.NewGuid(), this.ServiceName, item.Title, item.Link,
                Math.Clamp(score, 0.0, 1.0), now));
        }

        return surfacings.Count == 0
            ? ProactiveResult.quiet
            : new ProactiveResult([], surfacings, ProactiveStatus.Completed);
    }

    private static double Cosine(float[] left, float[] right)
    {
        var dot = 0.0;
        var leftNorm = 0.0;
        var rightNorm = 0.0;

        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        var denominator = Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm);
        return denominator == 0.0 ? 0.0 : dot / denominator;
    }
}
