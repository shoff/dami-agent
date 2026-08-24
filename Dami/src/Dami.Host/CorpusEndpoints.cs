using System.Text;
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
        app.MapPost("/ask", async (
            QuestionRequest request, IObservationEmbeddingStore store, IEmbeddingClient embedder,
            IRerankClient reranker, IChatClient chat, TimeProvider clock, CancellationToken token) =>
        {
            var context = (await SearchAsync(request.Question, store, embedder, reranker, token)
                .ConfigureAwait(false)).Take(RESULTS).ToList();
            if (context.Count == 0)
            {
                return Results.Ok(new { answer = (string?)null, sources = context });
            }

            var answer = await chat.CompleteAsync(
                BuildPrompt(request.Question, context, clock.GetUtcNow()), token).ConfigureAwait(false);
            return Results.Ok(new { answer = answer.Trim(), sources = context });
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

    private static string BuildPrompt(string question, List<Observation> context, DateTimeOffset today)
    {
        var prompt = new StringBuilder();
        prompt.Append("Today is ").Append(today.ToString("yyyy-MM-dd"))
            .AppendLine(". Observations carry their own dates; old ones are history, not the present.");
        prompt.AppendLine(
            "Answer the question using ONLY the numbered observations below, citing them like [2].");
        prompt.AppendLine(
            "If they do not contain the answer, say plainly that the memories do not cover it.");
        prompt.AppendLine("Be concise - a few sentences.");
        prompt.AppendLine();
        for (var index = 0; index < context.Count; index++)
        {
            prompt.Append(index + 1).Append(". [")
                .Append(context[index].OccurredAt.ToString("yyyy-MM-dd"))
                .Append("] ").AppendLine(context[index].Body);
        }

        prompt.AppendLine();
        prompt.Append("Question: ").AppendLine(question);
        return prompt.ToString();
    }
}
