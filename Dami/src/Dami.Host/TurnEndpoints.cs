using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
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
        app.MapPost("/turns", async (
            TurnRequest request, ITurnRunner runner, IFrontierChat frontier,
            IIdentityProvider identity, IExecutionEventStore events,
            Dami.Core.Frontier.AugmentedFrontierTurn augmentedTurn, TimeProvider clock,
            CancellationToken token) =>
        {
            if (request.Augmented)
            {
                return await AugmentedTurnAsync(request.Message, augmentedTurn, token)
                    .ConfigureAwait(false);
            }

            if (request.Frontier)
            {
                return await FrontierTurnAsync(
                    request.Message, frontier, identity, events, clock, token).ConfigureAwait(false);
            }

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

    /// <summary>
    /// Retrieval happens locally; the frontier answers on what the sidecar found. The
    /// local model is infrastructure here, not the brain.
    /// </summary>
    private static async Task<IResult> AugmentedTurnAsync(
        string message,
        Dami.Core.Frontier.AugmentedFrontierTurn augmentedTurn,
        CancellationToken cancellationToken)
    {
        var augmented = await augmentedTurn.RunAsync(message, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new
        {
            traceId = augmented.TraceId,
            answer = augmented.Answer,
            contextTokens = augmented.EstimatedTokens,
            beliefs = 0,
            memories = augmented.ContextItems,
            route = "Frontier (locally augmented)",
        });
    }

    /// <summary>
    /// A turn answered by the subscription frontier (ADR-0011) rather than the sidecar.
    /// It carries Dami's identity and the question — and deliberately no retrieved
    /// memory, which is what keeps it Egressable without a consent step. Memory-informed
    /// frontier work goes through the C4 brief flow instead, where Steve approves the
    /// exact bytes.
    /// </summary>
    private static async Task<IResult> FrontierTurnAsync(
        string message,
        IFrontierChat frontier,
        IIdentityProvider identity,
        IExecutionEventStore events,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        var spanId = Guid.NewGuid();
        await MarkAsync(events, traceId, spanId, clock, ExecutionEventType.TraceStarted,
            ExecutionStatus.Running, "frontier turn started", cancellationToken).ConfigureAwait(false);

        var answer = await frontier.CompleteAsync(
            new FrontierPrompt(
                $"{identity.FrontierVoice}\n\n{message}", "frontier chat turn",
                PrivacyClass.Egressable, traceId, ExecutionOrigin.UserTurn),
            cancellationToken).ConfigureAwait(false);

        await MarkAsync(events, traceId, spanId, clock, ExecutionEventType.TraceCompleted,
            ExecutionStatus.Succeeded, $"frontier turn: {answer.Length} chars", cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            traceId,
            answer,
            contextTokens = 0,
            beliefs = 0,
            memories = 0,
            route = "Frontier",
        });
    }

    private static Task MarkAsync(
        IExecutionEventStore events,
        Guid traceId,
        Guid spanId,
        TimeProvider clock,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        return events.AppendAsync(
            new ExecutionEvent(
                Guid.NewGuid(), traceId, spanId, null, ExecutionOrigin.UserTurn, "dami-host",
                type, status, clock.GetUtcNow(), label),
            cancellationToken);
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
