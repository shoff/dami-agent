using Dami.Contracts.Domains;

namespace Dami.Host;

/// <summary>The shared domain facts (K4). LocalOnly — served only on loopback.</summary>
public static class DomainEndpoints
{
    private const int LIMIT = 200;

    /// <summary>Maps the domain routes.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/domains", async (IDomainFactStore store, CancellationToken token) =>
            Results.Ok((await store.DomainsAsync(token).ConfigureAwait(false))
                .Select(domain => new { domain = domain.Domain, facts = domain.Facts })));

        app.MapGet("/domains/{domain}", async (string domain, IDomainFactStore store, CancellationToken token) =>
            Results.Ok(await Collect.ListAsync(store.TimelineAsync(domain.ToLowerInvariant(), LIMIT, token), token)
                .ConfigureAwait(false)));

        app.MapPost("/domains/facts/{prefix}/reject", async (
            string prefix, RejectDomainFactRequest request, IDomainFactStore store, CancellationToken token) =>
        {
            var recent = await Collect.ListAsync(store.TimelineAsync(null, LIMIT * 5, token), token).ConfigureAwait(false);
            var target = recent.FirstOrDefault(fact => Collect.Matches(fact.FactId, prefix));
            if (target is null)
            {
                return Results.NotFound();
            }

            await store.RejectAsync(target.FactId, request.Reason ?? "rejected by Steve", token).ConfigureAwait(false);
            return Results.Ok(new { rejected = target.Description });
        });
    }
}

/// <summary>Why a domain fact was wrong. Kept, so the correction has a record.</summary>
public sealed record RejectDomainFactRequest(string? Reason);
