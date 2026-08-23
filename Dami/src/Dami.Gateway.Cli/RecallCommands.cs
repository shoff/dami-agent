using Dami.Contracts.Memory;
using Dami.Contracts.Models;

namespace Dami.Gateway.Cli;

/// <summary>Semantic search over the corpus — §9.3 as a shell command.</summary>
/// <remarks>
/// embed (local) → pgvector ANN → cross-encoder rerank → top results with provenance.
/// Everything about the query stays on the host; the pipeline is the same one the
/// retrieval eval measures, so improvements there show up here.
/// </remarks>
public sealed class RecallCommands
{
    private const int CANDIDATES = 20;
    private const int RESULTS = 5;

    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly IRerankClient rerankClient;

    /// <summary>Creates the commands.</summary>
    public RecallCommands(
        IObservationEmbeddingStore embeddingStore,
        IEmbeddingClient embeddingClient,
        IRerankClient rerankClient)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(rerankClient);

        this.embeddingStore = embeddingStore;
        this.embeddingClient = embeddingClient;
        this.rerankClient = rerankClient;
    }

    /// <summary>Searches the corpus and prints the best matches, best first.</summary>
    public async Task<int> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryVector = (await this.embeddingClient
            .EmbedAsync([query], cancellationToken).ConfigureAwait(false))[0];

        var candidates = new List<Observation>();
        await foreach (var (observation, _) in this.embeddingStore
            .NearestAsync(queryVector, CANDIDATES, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(observation);
        }

        if (candidates.Count == 0)
        {
            Console.WriteLine("the corpus has no indexed observations yet - the embedder runs nightly");
            return 0;
        }

        await this.PrintRerankedAsync(query, candidates, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task PrintRerankedAsync(
        string query,
        List<Observation> candidates,
        CancellationToken cancellationToken)
    {
        var bodies = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            bodies.Add(candidate.Body);
        }

        var order = await this.rerankClient
            .RankAsync(query, bodies, cancellationToken).ConfigureAwait(false);

        var shown = 0;
        foreach (var index in order)
        {
            if (shown++ >= RESULTS)
            {
                break;
            }

            var observation = candidates[index];
            Console.WriteLine($"{observation.OccurredAt:yyyy-MM-dd}  [{observation.Source}]  {observation.Body}");
        }
    }
}
