using Dami.Core.Frontier;

namespace Dami.Host;

/// <summary>Read access to durable research artifacts under the Host's authentication policy.</summary>
public static class ResearchEndpoints
{
    /// <summary>Maps history and source-detail reads.</summary>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/research/runs", async (int? limit, ResearchJournal journal, CancellationToken token) =>
        {
            if (limit is < 1 or > 100)
            {
                return Results.BadRequest(new { error = "limit must be between 1 and 100" });
            }

            return Results.Ok(await journal.ListAsync(limit ?? 50, token).ConfigureAwait(false));
        });
        app.MapGet("/research/runs/{id:guid}", async (Guid id, ResearchJournal journal, CancellationToken token) =>
        {
            var run = await journal.GetAsync(id, token).ConfigureAwait(false);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });
    }
}
