using Dami.Contracts.Context;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Context;

/// <summary>Assembles turn context from the memory layer, within a hard token budget.</summary>
/// <remarks>
/// §9.2's discipline in code: only relevant retrieved memory, every item carrying
/// provenance, and a budget enforced at assembly time rather than audited afterwards.
/// The pipeline is the proven §9.3 spine — embed, ANN, rerank — over the same stores
/// everything else uses.
///
/// Token estimation is chars/4 — deliberately crude. The budget's job is preventing a
/// 90k-token prompt, not metering a 2,400-token one to the cent; a real tokenizer can
/// replace the estimate without touching callers.
/// </remarks>
public sealed class ContextBuilder : IContextBuilder
{
    private const int CHARS_PER_TOKEN = 4;

    /// <summary>How much of the shorter fact must already be said before it is a restatement.</summary>
    private const double RESTATEMENT_SHARE = 0.7;

    private static readonly char[] wordBreaks = [' ', ',', '.', ';', ':', '(', ')', '-', '\u2013', '/'];

    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IConclusionEmbeddingStore conclusionEmbeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly IRerankClient rerankClient;
    private readonly IConclusionLedger conclusionLedger;
    private readonly IQueryPlanner planner;
    private readonly ContextOptions contextOptions;
    private readonly QueryPlanOptions planOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ContextBuilder> logger;

    /// <summary>Creates the builder.</summary>
    public ContextBuilder(
        IObservationEmbeddingStore embeddingStore,
        IConclusionEmbeddingStore conclusionEmbeddingStore,
        IEmbeddingClient embeddingClient,
        IRerankClient rerankClient,
        IConclusionLedger conclusionLedger,
        IQueryPlanner planner,
        IOptions<ContextOptions> contextOptions,
        IOptions<QueryPlanOptions> planOptions,
        TimeProvider clock,
        ILogger<ContextBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(conclusionEmbeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(rerankClient);
        ArgumentNullException.ThrowIfNull(conclusionLedger);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(contextOptions);
        ArgumentNullException.ThrowIfNull(planOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.embeddingStore = embeddingStore;
        this.conclusionEmbeddingStore = conclusionEmbeddingStore;
        this.embeddingClient = embeddingClient;
        this.rerankClient = rerankClient;
        this.conclusionLedger = conclusionLedger;
        this.planner = planner;
        this.contextOptions = contextOptions.Value;
        this.planOptions = planOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<AssembledContext> BuildAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await this.planner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        var vectors = await this.embeddingClient
            .EmbedAsync(plan.Searches, cancellationToken).ConfigureAwait(false);

        // Beliefs answer to the request itself; a sub-query is a way of finding passages,
        // not a different question to hold opinions about.
        var beliefs = await this.CollectBeliefsAsync(vectors[0], cancellationToken).ConfigureAwait(false);
        var memories = await this.RetrieveMemoriesAsync(request, vectors, cancellationToken)
            .ConfigureAwait(false);
        var facts = Facts(plan);

        // Facts lead the memories: a domain row is a dated clinical statement, a memory is
        // the conversation that mentioned it. Per token the fact is worth more, and if the
        // budget runs out it is the prose that should be missing.
        facts.AddRange(memories);
        var assembled = Trim(beliefs, facts, this.contextOptions.MaxRetrievedTokens);

        this.logger.LogDebug(
            "Context: {Memories} memories, {Beliefs} beliefs, ~{Tokens} tokens from {Searches} search(es)",
            assembled.Memories.Count, assembled.Beliefs.Count, assembled.EstimatedTokens, plan.Searches.Count);

        return assembled;
    }

    /// <summary>The plan's resolved domain facts, as context items, near-duplicates dropped.</summary>
    /// <remarks>
    /// Domains deduplicate by exact text, which leaves restatements of one event holding
    /// separate slots: "Chest pain described as sharp, positional, and brief" and "Chest
    /// pain described as sharp and positional, with a spike lasting 30-40 seconds" are the
    /// same episode written twice, and together they spent two of the eight fact slots.
    /// Prose is left alone — this is the one place the redundancy holds.
    /// </remarks>
    private static List<RetrievedItem> Facts(QueryPlan plan)
    {
        var facts = new List<RetrievedItem>(plan.Facts.Count);
        var kept = new List<HashSet<string>>();
        foreach (var fact in plan.Facts)
        {
            var words = Words(fact.Text);
            if (kept.Any(earlier => Restates(words, earlier)))
            {
                continue;
            }

            kept.Add(words);

            // An undated fact says so. Stamping it with a stand-in date would read to the
            // frontier as a dated one, and the epoch reads as 1970.
            var when = fact.AsOf is null
                ? $"{fact.Kind}, date unknown"
                : $"{fact.Kind} {fact.AsOf:yyyy-MM-dd}";

            facts.Add(new RetrievedItem(
                "fact",
                fact.SourceId,
                $"[{when}] {fact.Text}",
                fact.AsOf is null
                    ? DateTimeOffset.MinValue
                    : new DateTimeOffset(fact.AsOf.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        return facts;
    }

    /// <summary>Whether a fact is mostly said already by one kept before it.</summary>
    /// <remarks>
    /// Containment rather than symmetric overlap, and measured against the shorter of the
    /// two: a restatement that adds a detail is still a restatement, while two genuinely
    /// different facts that share a subject ("aortic stenosis", "aortic valve replacement")
    /// diverge on the words that matter and stay.
    /// </remarks>
    private static bool Restates(HashSet<string> words, HashSet<string> earlier)
    {
        if (words.Count == 0 || earlier.Count == 0)
        {
            return false;
        }

        var shared = words.Count(word => earlier.Contains(word));
        return shared >= Math.Min(words.Count, earlier.Count) * RESTATEMENT_SHARE;
    }

    private static HashSet<string> Words(string text)
    {
        return text
            .Split(wordBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToLowerInvariant())
            .Where(word => word.Length > 2)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Beliefs by similarity to the request (D-009's second half), not the whole
    /// active set. Until the embedder has indexed any beliefs the store is empty,
    /// so retrieval falls back to the old subject-wholesale scan — beliefs must
    /// never silently vanish on migration day.
    /// </summary>
    private async Task<List<RetrievedItem>> CollectBeliefsAsync(
        float[] queryVector,
        CancellationToken cancellationToken)
    {
        var beliefs = new List<RetrievedItem>();
        var indexed = 0;
        await foreach (var (conclusion, distance) in this.conclusionEmbeddingStore
            .NearestAsync(queryVector, this.embeddingClient.ModelId, this.contextOptions.BeliefSlots, cancellationToken)
            .ConfigureAwait(false))
        {
            indexed++;
            if (distance > this.contextOptions.BeliefMaxDistance)
            {
                break;
            }

            beliefs.Add(new RetrievedItem(
                "belief", conclusion.ConclusionId, conclusion.Statement, conclusion.ConcludedAt));
        }

        if (indexed > 0)
        {
            return beliefs;
        }

        return await this.CollectBeliefsBySubjectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<RetrievedItem>> CollectBeliefsBySubjectAsync(CancellationToken cancellationToken)
    {
        var beliefs = new List<RetrievedItem>();
        await foreach (var conclusion in this.conclusionLedger
            .ActiveForSubjectAsync(this.contextOptions.Subject, cancellationToken).ConfigureAwait(false))
        {
            beliefs.Add(new RetrievedItem(
                "belief", conclusion.ConclusionId, conclusion.Statement, conclusion.ConcludedAt));
        }

        return beliefs;
    }

    /// <summary>Runs every planned search and reranks the union against the original request.</summary>
    /// <remarks>
    /// One embedding of one question retrieves whatever is nearest that phrasing, which is
    /// why "what should I ask the surgeon" returned long conversations that merely mention
    /// surgery. Several narrower searches surface several narrower passages; judging the
    /// pooled result against the actual question, not against the sub-query that found it,
    /// keeps expansion from rewarding drift.
    /// </remarks>
    private async Task<List<RetrievedItem>> RetrieveMemoriesAsync(
        string request,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<Guid, Observation>();
        foreach (var vector in vectors)
        {
            await this.GatherAsync(vector, candidates, cancellationToken).ConfigureAwait(false);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        return await this.RerankAsync(request, [.. candidates.Values], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task GatherAsync(
        float[] vector,
        Dictionary<Guid, Observation> candidates,
        CancellationToken cancellationToken)
    {
        var slots = this.planOptions.Enabled
            ? this.planOptions.SlotsPerSearch
            : this.contextOptions.Candidates;

        await foreach (var (observation, distance) in this.embeddingStore
            .NearestAsync(vector, this.embeddingClient.ModelId, slots, cancellationToken)
            .ConfigureAwait(false))
        {
            // The grounding gate: nearest-by-ranking is not the same as relevant, and a
            // window full of nearest junk reads as authority to the model.
            if (distance > this.contextOptions.MaxDistance)
            {
                break;
            }

            candidates.TryAdd(observation.ObservationId, observation);
        }
    }

    private async Task<List<RetrievedItem>> RerankAsync(
        string request,
        List<Observation> candidates,
        CancellationToken cancellationToken)
    {
        var bodies = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            bodies.Add(candidate.Body);
        }

        var order = await this.rerankClient
            .RankAsync(request, bodies, cancellationToken).ConfigureAwait(false);

        return this.SelectMemories(candidates, order);
    }

    /// <summary>Relevance order, with slots reserved for the most recent relevant items.</summary>
    /// <remarks>
    /// Observed failure this guards against: pure relevance filled the whole window with
    /// five-month-old crisis memories and the model answered as if the crisis were
    /// current. Recent items enter first (newest of the candidate pool inside the
    /// window), the rest by rerank order.
    /// </remarks>
    private List<RetrievedItem> SelectMemories(List<Observation> candidates, IReadOnlyList<int> order)
    {
        var chosen = this.TakeRecent(candidates);

        foreach (var index in order)
        {
            if (chosen.Count >= this.contextOptions.MaxMemories)
            {
                break;
            }

            if (!chosen.Contains(candidates[index]))
            {
                chosen.Add(candidates[index]);
            }
        }

        var memories = new List<RetrievedItem>(chosen.Count);
        foreach (var observation in chosen)
        {
            memories.Add(new RetrievedItem(
                "observation", observation.ObservationId, observation.Body, observation.OccurredAt));
        }

        return memories;
    }

    private List<Observation> TakeRecent(List<Observation> candidates)
    {
        var chosen = new List<Observation>(this.contextOptions.MaxMemories);
        var cutoff = this.clock.GetUtcNow().AddDays(-this.contextOptions.RecentDays);

        var byAge = new List<Observation>(candidates);
        byAge.Sort((left, right) => right.OccurredAt.CompareTo(left.OccurredAt));
        foreach (var candidate in byAge)
        {
            if (chosen.Count >= this.contextOptions.RecentSlots || candidate.OccurredAt < cutoff)
            {
                break;
            }

            chosen.Add(candidate);
        }

        return chosen;
    }

    /// <summary>Applies the budget: beliefs first, then memories by relevance until it is spent.</summary>
    /// <remarks>
    /// Beliefs win the budget contest deliberately — the active set is small by design
    /// (D-009) and identity continuity is the product; a memory can be re-retrieved
    /// next turn, a forgotten belief is a personality change.
    /// </remarks>
    private static AssembledContext Trim(
        List<RetrievedItem> beliefs,
        List<RetrievedItem> memories,
        int maxTokens)
    {
        var spent = 0;
        var keptBeliefs = Fill(beliefs, maxTokens, ref spent);
        var keptMemories = Fill(memories, maxTokens, ref spent);
        return new AssembledContext(keptMemories, keptBeliefs, spent);
    }

    /// <summary>Takes what fits, in order, skipping what does not.</summary>
    /// <remarks>
    /// Skip, do not stop. Stopping at the first item too large to fit let one long
    /// conversation summary end the list while several short precise facts behind it
    /// would each have fitted easily.
    /// </remarks>
    private static List<RetrievedItem> Fill(List<RetrievedItem> items, int maxTokens, ref int spent)
    {
        var kept = new List<RetrievedItem>();
        foreach (var item in items)
        {
            var cost = Cost(item);
            if (spent + cost > maxTokens)
            {
                continue;
            }

            spent += cost;
            kept.Add(item);
        }

        return kept;
    }

    private static int Cost(RetrievedItem item)
    {
        return item.Content.Length / CHARS_PER_TOKEN + 8;
    }
}
