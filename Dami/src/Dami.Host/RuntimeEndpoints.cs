using System.Runtime.CompilerServices;
using Dami.Contracts.Approvals;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Dami.Core.Turns;

namespace Dami.Host;

/// <summary>The runtime surface (D-005). Every response is rendered from durable state.</summary>
public static class RuntimeEndpoints
{
    private const int PAGE = 50;

    /// <summary>Maps every endpoint. The CLI's verb families, as routes.</summary>
    public static void MapDamiRuntime(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        MapTurns(app);
        MapSurfacings(app);
        MapBeliefs(app);
        MapApprovals(app);
        MapEvents(app);
    }

    private static void MapTurns(WebApplication app)
    {
        app.MapPost("/turns", async (TurnRequest request, ITurnRunner runner, CancellationToken token) =>
        {
            var result = await runner.RunAsync(request.Message, token).ConfigureAwait(false);
            return Results.Ok(new
            {
                traceId = result.TraceId,
                answer = result.Answer,
                contextTokens = result.Context.EstimatedTokens,
                route = result.Route.Tier.ToString(),
            });
        });

        app.MapPost("/turns/stream", async (
            TurnRequest request, ITurnRunner runner, HttpContext http, CancellationToken token) =>
        {
            var stream = await runner.BeginStreamingAsync(request.Message, token).ConfigureAwait(false);
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.Append("X-Dami-Trace", stream.TraceId.ToString("N"));
            await foreach (var fragment in stream.Tokens.WithCancellation(token).ConfigureAwait(false))
            {
                await http.Response.WriteAsync($"data: {fragment.Replace("\n", "\ndata: ")}\n\n", token)
                    .ConfigureAwait(false);
                await http.Response.Body.FlushAsync(token).ConfigureAwait(false);
            }
        });
    }

    private static void MapSurfacings(WebApplication app)
    {
        app.MapGet("/surfacings", (ISurfacingQueue queue, CancellationToken token) =>
            Results.Ok(CollectAsync(queue.PendingAsync(PAGE, token))));

        app.MapPost("/surfacings/{id:guid}/feedback", async (
            Guid id, FeedbackRequest request, ISurfacingQueue queue,
            TimeProvider clock, CancellationToken token) =>
        {
            await queue.RecordFeedbackAsync(id, request.Feedback, clock.GetUtcNow(), token)
                .ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    private static void MapBeliefs(WebApplication app)
    {
        app.MapGet("/beliefs", (IConclusionLedger ledger, CancellationToken token) =>
            Results.Ok(CollectAsync(ledger.ActiveForSubjectAsync("steve", token))));
    }

    private static void MapApprovals(WebApplication app)
    {
        app.MapGet("/approvals", (IApprovalService approvals, CancellationToken token) =>
            Results.Ok(CollectAsync(approvals.PendingAsync(token))));

        app.MapPost("/approvals/{id:guid}/resolve", async (
            Guid id, ResolveRequest request, IApprovalService approvals,
            TimeProvider clock, CancellationToken token) =>
        {
            var status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Denied;
            var resolved = await approvals.ResolveAsync(
                id, status, request.Note ?? "resolved via API", clock.GetUtcNow(), token)
                .ConfigureAwait(false);
            return resolved ? Results.NoContent() : Results.Conflict();
        });
    }

    private static void MapEvents(WebApplication app)
    {
        app.MapGet("/traces/{id:guid}", (Guid id, IExecutionEventStore store, CancellationToken token) =>
            Results.Ok(CollectAsync(store.ReplayAsync(id, token))));

        // The GUI's live feed: poll with the last sequence seen.
        app.MapGet("/events", (long after, IExecutionEventStore store, CancellationToken token) =>
            Results.Ok(CollectAsync(store.ReadSinceAsync(after, PAGE, token))));
    }

    private static async IAsyncEnumerable<T> CollectAsync<T>(
        IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}

/// <summary>One interactive turn.</summary>
public sealed record TurnRequest(string Message);

/// <summary>A reaction to a surfacing — "good: …", "bad: …", "meh".</summary>
public sealed record FeedbackRequest(string Feedback);

/// <summary>Approve or deny one pending approval.</summary>
public sealed record ResolveRequest(bool Approve, string? Note);
