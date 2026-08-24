using Dami.Core.Turns;

namespace Dami.Host;

/// <summary>Interactive turns: whole answers and token streams.</summary>
public static class TurnEndpoints
{
    /// <summary>Maps the turn routes.</summary>
    public static void Map(WebApplication app)
    {
        MapWhole(app);
        MapStream(app);
    }

    private static void MapWhole(WebApplication app)
    {
        app.MapPost("/turns", async (TurnRequest request, ITurnRunner runner, CancellationToken token) =>
        {
            var result = await runner.RunAsync(request.Message, token).ConfigureAwait(false);
            return Results.Ok(new
            {
                traceId = result.TraceId,
                answer = result.Answer,
                contextTokens = result.Context.EstimatedTokens,
                beliefs = result.Context.Beliefs.Count,
                memories = result.Context.Memories.Count,
                route = result.Route.Tier.ToString(),
            });
        });

    }

    private static void MapStream(WebApplication app)
    {
        app.MapPost("/turns/stream", async (
            TurnRequest request, ITurnRunner runner, HttpContext http, CancellationToken token) =>
        {
            var stream = await runner.BeginStreamingAsync(request.Message, token).ConfigureAwait(false);
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.Append("X-Dami-Trace", stream.TraceId.ToString("N"));
            http.Response.Headers.Append("X-Dami-Route", stream.Route.Tier.ToString());
            http.Response.Headers.Append("X-Dami-Ctx-Tokens", stream.Context.EstimatedTokens.ToString());
            http.Response.Headers.Append("X-Dami-Memories", stream.Context.Memories.Count.ToString());
            http.Response.Headers.Append("X-Dami-Beliefs", stream.Context.Beliefs.Count.ToString());
            await foreach (var fragment in stream.Tokens.WithCancellation(token).ConfigureAwait(false))
            {
                await http.Response.WriteAsync($"data: {fragment.Replace("\n", "\ndata: ")}\n\n", token)
                    .ConfigureAwait(false);
                await http.Response.Body.FlushAsync(token).ConfigureAwait(false);
            }
        });
    }
}
