using Dami.Contracts.Memory;

namespace Dami.Host;

/// <summary>The ledger, readable and correctable over the wire (F-09/F-10).</summary>
public static class BeliefEndpoints
{
    /// <summary>Maps the belief routes.</summary>
    public static void Map(WebApplication app)
    {
        MapReads(app);
        MapCorrections(app);
        MapNotes(app);
    }

    private static void MapReads(WebApplication app)
    {
        app.MapGet("/beliefs", (
            DateTimeOffset? asOf, IConclusionLedger ledger, TimeProvider clock,
            CancellationToken token) =>
            Results.Ok(Collect.Async(ledger.ActiveAsOfAsync(asOf ?? clock.GetUtcNow(), token))));

        app.MapGet("/beliefs/diff", async (
            DateTimeOffset from, DateTimeOffset? to, IConclusionLedger ledger,
            TimeProvider clock, CancellationToken token) =>
        {
            var before = await Collect.ListAsync(ledger.ActiveAsOfAsync(from, token), token)
                .ConfigureAwait(false);
            var after = await Collect.ListAsync(
                ledger.ActiveAsOfAsync(to ?? clock.GetUtcNow(), token), token).ConfigureAwait(false);
            var beforeIds = before.Select(item => item.ConclusionId).ToHashSet();
            var afterIds = after.Select(item => item.ConclusionId).ToHashSet();
            return Results.Ok(new
            {
                added = after.Where(item => !beforeIds.Contains(item.ConclusionId)),
                removed = before.Where(item => !afterIds.Contains(item.ConclusionId)),
            });
        });

    }

    private static void MapCorrections(WebApplication app)
    {
        MapRetract(app);
        MapCorrect(app);
    }

    private static void MapRetract(WebApplication app)
    {
        app.MapPost("/beliefs/{prefix}/retract", async (
            string prefix, RetractRequest request, IConclusionLedger ledger,
            TimeProvider clock, CancellationToken token) =>
        {
            var target = await ResolveAsync(ledger, clock, prefix, token).ConfigureAwait(false);
            if (target is null)
            {
                return Results.NotFound();
            }

            await ledger.RetractAsync(target.ConclusionId, request.Reason, clock.GetUtcNow(), token)
                .ConfigureAwait(false);
            return Results.Ok(new { retracted = target.Statement, reason = request.Reason });
        });

    }

    private static void MapCorrect(WebApplication app)
    {
        app.MapPost("/beliefs/{prefix}/correct", async (
            string prefix, CorrectRequest request, IConclusionLedger ledger,
            TimeProvider clock, CancellationToken token) =>
        {
            var target = await ResolveAsync(ledger, clock, prefix, token).ConfigureAwait(false);
            if (target is null)
            {
                return Results.NotFound();
            }

            // F-10: corrections supersede rather than coexist, and a direct correction
            // outranks any inference.
            var replacement = new Conclusion(
                Guid.NewGuid(), target.ConclusionId, target.Subject, request.Statement,
                1.0, ConclusionSource.Correction, clock.GetUtcNow(), target.SupportingObservations);
            await ledger.SupersedeAsync(replacement, "corrected by Steve", token).ConfigureAwait(false);
            return Results.Ok(new { was = target.Statement, now = replacement.Statement });
        });

    }

    private static void MapNotes(WebApplication app)
    {
        app.MapPost("/observations", async (
            NoteRequest request, IObservationCorpus corpus, TimeProvider clock,
            CancellationToken token) =>
        {
            var observation = new Observation(Guid.NewGuid(), clock.GetUtcNow(), "cli-note", request.Body);
            await corpus.RecordAsync(observation, token).ConfigureAwait(false);
            return Results.Ok(new { observationId = observation.ObservationId });
        });
    }

    private static async Task<Conclusion?> ResolveAsync(
        IConclusionLedger ledger,
        TimeProvider clock,
        string prefix,
        CancellationToken cancellationToken)
    {
        await foreach (var conclusion in ledger.ActiveAsOfAsync(clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false))
        {
            if (Collect.Matches(conclusion.ConclusionId, prefix))
            {
                return conclusion;
            }
        }

        return null;
    }
}
