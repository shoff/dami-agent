using Dami.Contracts.Privacy;

namespace Dami.Host;

/// <summary>What the gate decided, and Steve's corrections (G9a). LocalOnly — loopback only.</summary>
public static class DisclosureEndpoints
{
    private const int DEFAULT_LIMIT = 50;
    private const int MAX_LIMIT = 500;

    /// <summary>Maps the disclosure routes.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/disclosures", async (int? limit, IDisclosureLedger ledger, CancellationToken token) =>
        {
            var count = limit ?? DEFAULT_LIMIT;
            if (count is < 1 or > MAX_LIMIT)
            {
                return Results.BadRequest(new { error = $"limit must be between 1 and {MAX_LIMIT}" });
            }

            return Results.Ok(await ledger.RecentAsync(count, token).ConfigureAwait(false));
        });
        MapCorrect(app);
    }

    private static void MapCorrect(WebApplication app)
    {
        app.MapPost("/disclosures/{prefix}/correct", async (
            string prefix, CorrectDisclosureRequest request, IDisclosureLedger ledger,
            TimeProvider clock, CancellationToken token) =>
        {
            if (!Enum.TryParse<Disclosure>(request.Disclosure, ignoreCase: true, out var corrected))
            {
                return Results.BadRequest(new { error = "disclosure must be pass, disguise, or withhold" });
            }

            var recent = await ledger.RecentAsync(MAX_LIMIT, token).ConfigureAwait(false);
            var target = recent.FirstOrDefault(item => Collect.Matches(item.DecisionId, prefix));
            if (target is null)
            {
                return Results.NotFound();
            }

            var correction = new DisclosureCorrection(
                corrected, request.Note ?? string.Empty, request.CorrectedBy ?? "steve", clock.GetUtcNow());
            var recorded = await ledger.CorrectAsync(target.DecisionId, correction, token).ConfigureAwait(false);
            return recorded
                ? Results.Ok(new { corrected = target.DecisionId, was = target.Disclosure.ToString(), now = corrected.ToString() })
                : Results.Conflict(new { error = "that decision was already corrected" });
        });
    }
}

/// <summary>What the gate should have decided, and why. The why is what teaches it.</summary>
public sealed record CorrectDisclosureRequest(string Disclosure, string? Note, string? CorrectedBy);
