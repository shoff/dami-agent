using Dami.Contracts.Domains;

namespace Dami.Host;

/// <summary>The fitness domain (H9/G14). LocalOnly — served only on loopback.</summary>
public static class FitnessEndpoints
{
    /// <summary>Maps the fitness route.</summary>
    public static void Map(WebApplication app)
    {
        // The whole domain in one response, deliberately: a few hundred rows, and a
        // client holding all of them can recompute any view without another request.
        app.MapGet("/fitness", async (IFitnessStore store, CancellationToken token) =>
            Results.Ok(await store.SnapshotAsync(token).ConfigureAwait(false)));
    }
}
