using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Contracts.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Opportunities;

/// <summary>Looks for work, bounties and listings worth Steve's time, and says so once a week (ADR-0033).</summary>
/// <remarks>
/// It searches, it does not act: every hit is ranked against the profile by the local
/// cross-encoder, the best few become one surfacing with their addresses, and what to do
/// about any of them is a conversation. Nothing retrieved from the corpus is in the
/// queries; they are Steve's own words from the drop-in.
/// </remarks>
public sealed class OpportunityScoutService : IProactiveService
{
    private readonly ISearchEngine engine;
    private readonly IRerankClient reranker;
    private readonly OpportunityScoutOptions scoutOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<OpportunityScoutService> logger;

    /// <summary>Creates the scout.</summary>
    public OpportunityScoutService(
        ISearchEngine engine,
        IRerankClient reranker,
        IOptions<OpportunityScoutOptions> scoutOptions,
        TimeProvider clock,
        ILogger<OpportunityScoutService> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentNullException.ThrowIfNull(scoutOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.engine = engine;
        this.reranker = reranker;
        this.scoutOptions = scoutOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "opportunity-scout";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!this.scoutOptions.Enabled || this.scoutOptions.Queries.Count == 0 || string.IsNullOrWhiteSpace(this.scoutOptions.Profile))
        {
            return ProactiveResult.quiet;
        }

        var hits = await this.GatherAsync(cancellationToken).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return ProactiveResult.Did("no results from any query");
        }

        var ranked = await this.RankAsync(hits, cancellationToken).ConfigureAwait(false);
        var chosen = ranked.Take(this.scoutOptions.MaxSurfaced).ToList();
        this.logger.LogInformation("Opportunity scout: {Hits} hit(s) from {Queries} quer(ies), {Chosen} surfaced",
            hits.Count, this.scoutOptions.Queries.Count, chosen.Count);
        return new ProactiveResult(
            Array.Empty<Conclusion>(),
            [new Surfacing(Guid.NewGuid(), this.ServiceName, "Opportunities this week", Digest(chosen),
                this.scoutOptions.Confidence, this.clock.GetUtcNow())],
            ProactiveStatus.Completed,
            $"{chosen.Count} of {hits.Count} surfaced");
    }

    /// <summary>The digest: one line of title, address, and snippet per hit, best first.</summary>
    public static string Digest(IReadOnlyList<SearchResult> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        return string.Join("\n\n", chosen.Select((hit, index) =>
            $"{index + 1}. {hit.Title}\n{hit.Url}\n{hit.Snippet}".TrimEnd()));
    }

    private async Task<List<SearchResult>> GatherAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<SearchResult>();
        foreach (var query in this.scoutOptions.Queries)
        {
            IReadOnlyList<SearchResult> found;
            try
            {
                found = await this.engine.SearchAsync(query, this.scoutOptions.ResultsPerQuery, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning(exception, "Opportunity scout: search failed for one query");
                continue;
            }

            hits.AddRange(found.Where(hit => seen.Add(hit.Url.AbsoluteUri)));
        }

        return hits;
    }

    private async Task<IReadOnlyList<SearchResult>> RankAsync(List<SearchResult> hits, CancellationToken cancellationToken)
    {
        try
        {
            var order = await this.reranker.RankAsync(
                this.scoutOptions.Profile, hits.Select(hit => hit.Title + " — " + hit.Snippet).ToList(), cancellationToken)
                .ConfigureAwait(false);
            return order.Select(position => hits[position]).ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Opportunity scout: reranker unavailable; keeping search order");
            return hits;
        }
    }
}
