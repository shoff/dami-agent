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

    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IConclusionEmbeddingStore conclusionEmbeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly IRerankClient rerankClient;
    private readonly IConclusionLedger conclusionLedger;
    private readonly ContextOptions contextOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ContextBuilder> logger;

    /// <summary>Creates the builder.</summary>
    public ContextBuilder(
        IObservationEmbeddingStore embeddingStore,
        IConclusionEmbeddingStore conclusionEmbeddingStore,
        IEmbeddingClient embeddingClient,
        IRerankClient rerankClient,
        IConclusionLedger conclusionLedger,
        IOptions<ContextOptions> contextOptions,
        TimeProvider clock,
        ILogger<ContextBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(conclusionEmbeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(rerankClient);
        ArgumentNullException.ThrowIfNull(conclusionLedger);
        ArgumentNullException.ThrowIfNull(contextOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.embeddingStore = embeddingStore;
        this.conclusionEmbeddingStore = conclusionEmbeddingStore;
        this.embeddingClient = embeddingClient;
        this.rerankClient = rerankClient;
        this.conclusionLedger = conclusionLedger;
        this.contextOptions = contextOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<AssembledContext> BuildAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var queryVector = (await this.embeddingClient
            .EmbedAsync([request], cancellationToken).ConfigureAwait(false))[0];

        var beliefs = await this.CollectBeliefsAsync(queryVector, cancellationToken).ConfigureAwait(false);
        var memories = await this.RetrieveMemoriesAsync(request, queryVector, cancellationToken)
            .ConfigureAwait(false);

        var assembled = Trim(beliefs, memories, this.contextOptions.MaxRetrievedTokens);

        this.logger.LogDebug(
            "Context: {Memories} memories, {Beliefs} beliefs, ~{Tokens} tokens",
            assembled.Memories.Count, assembled.Beliefs.Count, assembled.EstimatedTokens);

        return assembled;
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

    private async Task<List<RetrievedItem>> RetrieveMemoriesAsync(
        string request,
        float[] queryVector,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Observation>();
        await foreach (var (observation, distance) in this.embeddingStore
            .NearestAsync(queryVector, this.embeddingClient.ModelId, this.contextOptions.Candidates, cancellationToken)
            .ConfigureAwait(false))
        {
            // The grounding gate: nearest-by-ranking is not the same as relevant, and a
            // window full of nearest junk reads as authority to the model.
            if (distance > this.contextOptions.MaxDistance)
            {
                break;
            }

            candidates.Add(observation);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        return await this.RerankAsync(request, candidates, cancellationToken).ConfigureAwait(false);
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
        var keptBeliefs = new List<RetrievedItem>();
        foreach (var belief in beliefs)
        {
            var cost = Cost(belief);
            if (spent + cost > maxTokens)
            {
                break;
            }

            spent += cost;
            keptBeliefs.Add(belief);
        }

        var keptMemories = new List<RetrievedItem>();
        foreach (var memory in memories)
        {
            var cost = Cost(memory);
            if (spent + cost > maxTokens)
            {
                break;
            }

            spent += cost;
            keptMemories.Add(memory);
        }

        return new AssembledContext(keptMemories, keptBeliefs, spent);
    }

    private static int Cost(RetrievedItem item)
    {
        return item.Content.Length / CHARS_PER_TOKEN + 8;
    }
}
