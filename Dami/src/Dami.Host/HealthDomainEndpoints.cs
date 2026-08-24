using Dami.Contracts.Domains;

namespace Dami.Host;

/// <summary>The health domain timeline (K2). LocalOnly — served only on loopback.</summary>
public static class HealthDomainEndpoints
{
    /// <summary>Maps the health route.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/health-log", (IHealthEventStore store, CancellationToken token) =>
            Results.Ok(Collect.Async(store.TimelineAsync(100, token))));
    }
}
