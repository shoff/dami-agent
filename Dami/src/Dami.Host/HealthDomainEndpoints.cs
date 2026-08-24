using Dami.Contracts.Domains;

namespace Dami.Host;

/// <summary>The health domain timeline (K2). LocalOnly — served only on loopback.</summary>
public static class HealthDomainEndpoints
{
    /// <summary>Maps the health route.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/health-log", async (IHealthEventStore store, CancellationToken token) =>
        {
            // The store deduplicates by wording (keeping the earliest occurrence), which
            // means it cannot also return date order; present it newest-first here.
            var timeline = await Collect.ListAsync(store.TimelineAsync(200, token), token)
                .ConfigureAwait(false);
            return Results.Ok(timeline.OrderByDescending(item => item.EventDate));
        });
    }
}
