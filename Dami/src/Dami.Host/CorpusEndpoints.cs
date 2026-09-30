using Dami.Contracts.Context;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;

namespace Dami.Host;

/// <summary>Corpus search, cited answers, and the context preview.</summary>
public static class CorpusEndpoints
{
    private const int CANDIDATES = 24;
    private const int RESULTS = 8;

    /// <summary>Maps the corpus routes.</summary>
    public static void Map(WebApplication app)
    {
        MapSearchRoutes(app);
        MapAsk(app);
    }

    private static void MapSearchRoutes(WebApplication app)
    {
        app.MapGet("/recall", async (
            string q, IObservationEmbeddingStore store, IEmbeddingClient embedder,
            IRerankClient reranker, CancellationToken token) =>
        {
            var reranked = await SearchAsync(q, store, embedder, reranker, token).ConfigureAwait(false);
            return Results.Ok(reranked.Take(RESULTS));
        });

        app.MapGet("/context", async (string q, IContextBuilder builder, CancellationToken token) =>
        {
            var context = await builder.BuildAsync(q, token).ConfigureAwait(false);
            return Results.Ok(new
            {
                estimatedTokens = context.EstimatedTokens,
                beliefs = context.Beliefs,
                memories = context.Memories,
            });
        });

    }

    private static void MapAsk(WebApplication app)
    {
        // #3 "ask your life anything": the corpus and Steve's structured records, answered by the
        // local model with numbered sources. The response shape is unchanged for `dami ask`.
        app.MapPost("/ask", async (QuestionRequest request, Dami.Core.Life.LifeAnswerer answerer, CancellationToken token) =>
        {
            var answer = await answerer.AnswerAsync(request.Question, token).ConfigureAwait(false);
            return Results.Ok(new
            {
                answer = answer.Answer,
                sources = answer.Sources.Select(source => new { occurredAt = source.At, source = source.Kind, body = source.Text }),
            });
        });
    }

    private static async Task<List<Observation>> SearchAsync(
        string query,
        IObservationEmbeddingStore store,
        IEmbeddingClient embedder,
        IRerankClient reranker,
        CancellationToken cancellationToken)
    {
        var queryVector = (await embedder.EmbedAsync([query], cancellationToken).ConfigureAwait(false))[0];
        var candidates = new List<Observation>();
        await foreach (var (observation, _) in store
            .NearestAsync(queryVector, embedder.ModelId, CANDIDATES, cancellationToken)
            .ConfigureAwait(false))
        {
            candidates.Add(observation);
        }

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var order = await reranker.RankAsync(
            query, candidates.Select(item => item.Body).ToList(), cancellationToken)
            .ConfigureAwait(false);
        return order.Select(index => candidates[index]).ToList();
    }
}
