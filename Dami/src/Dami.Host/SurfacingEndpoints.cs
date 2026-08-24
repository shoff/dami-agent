using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;

namespace Dami.Host;

/// <summary>The surfacing queue: list, read, react. Reactions also join the corpus.</summary>
public static class SurfacingEndpoints
{
    private const int PAGE = 20;

    /// <summary>Maps the surfacing routes.</summary>
    public static void Map(WebApplication app)
    {
        MapLists(app);
        MapRead(app);
        MapFeedback(app);
    }

    private static void MapLists(WebApplication app)
    {
        app.MapGet("/surfacings", (ISurfacingQueue queue, CancellationToken token) =>
            Results.Ok(Collect.Async(queue.PendingAsync(PAGE, token))));

        app.MapGet("/surfacings/recent", (ISurfacingQueue queue, CancellationToken token) =>
            Results.Ok(Collect.Async(queue.RecentAsync(PAGE, token))));

    }

    private static void MapRead(WebApplication app)
    {
        app.MapGet("/surfacings/{prefix}", async (
            string prefix, ISurfacingQueue queue, TimeProvider clock, CancellationToken token) =>
        {
            var surfacing = await ResolveAsync(queue, prefix, token).ConfigureAwait(false);
            if (surfacing is null)
            {
                return Results.NotFound();
            }

            await queue.DeliverAsync(surfacing.SurfacingId, clock.GetUtcNow(), token)
                .ConfigureAwait(false);
            return Results.Ok(surfacing);
        });

    }

    private static void MapFeedback(WebApplication app)
    {
        app.MapPost("/surfacings/{prefix}/feedback", async (
            string prefix, FeedbackRequest request, ISurfacingQueue queue,
            IObservationCorpus corpus, TimeProvider clock, CancellationToken token) =>
        {
            var surfacing = await ResolveAsync(queue, prefix, token).ConfigureAwait(false);
            if (surfacing is null)
            {
                return Results.NotFound();
            }

            var feedback = request.Note is null ? request.Verdict : $"{request.Verdict}: {request.Note}";
            var reactedAt = clock.GetUtcNow();
            await queue.RecordFeedbackAsync(surfacing.SurfacingId, feedback, reactedAt, token)
                .ConfigureAwait(false);

            // A reaction is itself something that happened, so it joins the corpus — which
            // is how the reflection pass gets to notice patterns in what Steve values.
            await corpus.RecordAsync(
                new Observation(
                    Guid.NewGuid(), reactedAt, "surfacing-feedback",
                    $"rated the surfacing '{surfacing.Title}' {feedback}"),
                token).ConfigureAwait(false);
            return Results.Ok(new { surfacingId = surfacing.SurfacingId, feedback });
        });
    }

    private static async Task<Surfacing?> ResolveAsync(
        ISurfacingQueue queue,
        string prefix,
        CancellationToken cancellationToken)
    {
        await foreach (var surfacing in queue.RecentAsync(100, cancellationToken).ConfigureAwait(false))
        {
            if (Collect.Matches(surfacing.SurfacingId, prefix))
            {
                return surfacing;
            }
        }

        return null;
    }
}
