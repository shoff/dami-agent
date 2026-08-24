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

        app.MapPost("/health-log/{prefix}/reject", async (
            string prefix, RejectHealthFactRequest request, IHealthEventStore store,
            CancellationToken token) =>
        {
            var timeline = await Collect.ListAsync(store.TimelineAsync(200, token), token)
                .ConfigureAwait(false);
            var target = timeline.FirstOrDefault(
                item => Collect.Matches(item.HealthEventId, prefix));
            if (target is null)
            {
                return Results.NotFound();
            }

            await store.RejectAsync(
                target.ObservationId, target.Description,
                request.Reason ?? "rejected by Steve", token).ConfigureAwait(false);
            return Results.Ok(new { rejected = target.Description });
        });
    }
}

/// <summary>Why a health fact was wrong. Kept, so the correction has a record.</summary>
public sealed record RejectHealthFactRequest(string? Reason);
