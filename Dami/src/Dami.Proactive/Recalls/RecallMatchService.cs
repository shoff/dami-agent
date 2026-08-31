using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Recalls;

/// <summary>Joins recorded recalls against the health domain, locally (H12, local half).</summary>
/// <remarks>
/// This half reads health data and therefore, by the recorded D-012 rule, holds no
/// egress client at all — it cannot transmit, so what it knows cannot leave. Medication
/// names are derived at runtime from the health timeline; the configured watch terms
/// cover what structured data cannot name (the valve). A match surfaces once; the match
/// itself is recorded as a fact so the next pass knows.
/// </remarks>
public sealed class RecallMatchService : IProactiveService
{
    private const string DOMAIN = "recall";
    private const string MATCH_CATEGORY = "match";
    private const int HEALTH_LIMIT = 500;
    private const int RECALL_LIMIT = 800;
    private const int MAX_SURFACINGS_PER_PASS = 3;

    private readonly IHealthEventStore health;
    private readonly IDomainFactStore store;
    private readonly RecallSentinelOptions sentinelOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<RecallMatchService> logger;

    /// <summary>Creates the service.</summary>
    public RecallMatchService(
        IHealthEventStore health,
        IDomainFactStore store,
        IOptions<RecallSentinelOptions> sentinelOptions,
        TimeProvider clock,
        ILogger<RecallMatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sentinelOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.health = health;
        this.store = store;
        this.sentinelOptions = sentinelOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "recall-match";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var terms = await this.TermsAsync(cancellationToken).ConfigureAwait(false);
        var (recalls, known) = await this.RecallsAsync(cancellationToken).ConfigureAwait(false);
        var surfacings = new List<Surfacing>();
        var written = 0;
        foreach (var recall in recalls)
        {
            written += await this.MatchOneAsync(recall, terms, known, surfacings, cancellationToken)
                .ConfigureAwait(false)
                ? 1
                : 0;
        }

        this.logger.LogInformation(
            "Recall match: {Terms} term(s), {Matches} new match(es)", terms.Count, written);
        return surfacings.Count == 0
            ? ProactiveResult.Did($"{terms.Count} term(s) against {recalls.Count} recall(s), no news")
            : new ProactiveResult(
                Array.Empty<Conclusion>(), surfacings, ProactiveStatus.Completed,
                $"{written} recall match(es) surfaced");
    }

    private async Task<bool> MatchOneAsync(
        DomainFact recall,
        HashSet<string> terms,
        HashSet<string> known,
        List<Surfacing> surfacings,
        CancellationToken cancellationToken)
    {
        var term = RecallTerms.Mentions(recall.Description, terms);
        if (term is null)
        {
            return false;
        }

        var description = $"matches '{term}': {recall.Description}";
        if (known.Contains(description))
        {
            return false;
        }

        var recorded = await this.store.RecordAsync(
            new DomainFact(
                Guid.NewGuid(), DOMAIN, recall.AsOf, MATCH_CATEGORY, description,
                this.ServiceName, this.clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (!recorded)
        {
            return false;
        }

        known.Add(description);
        this.TrySurface(surfacings, term, recall.Description);
        return true;
    }

    private void TrySurface(List<Surfacing> surfacings, string term, string recall)
    {
        if (surfacings.Count < MAX_SURFACINGS_PER_PASS)
        {
            surfacings.Add(new Surfacing(
                Guid.NewGuid(), this.ServiceName,
                $"Recall may affect you: {term}", recall,
                this.sentinelOptions.Confidence, this.clock.GetUtcNow()));
        }
    }

    /// <summary>Medication names from the health timeline plus the configured watch terms.</summary>
    private async Task<HashSet<string>> TermsAsync(CancellationToken cancellationToken)
    {
        var medications = new List<string>();
        await foreach (var healthEvent in this.health
            .TimelineAsync(HEALTH_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            if (healthEvent.Category == HealthCategory.Medication)
            {
                medications.Add(healthEvent.Description);
            }
        }

        var terms = RecallTerms.FromMedications(medications);
        foreach (var term in this.sentinelOptions.WatchTerms)
        {
            terms.Add(term.ToLowerInvariant());
        }

        return terms;
    }

    /// <summary>The recorded recalls to judge, and the matches already made.</summary>
    private async Task<(List<DomainFact> Recalls, HashSet<string> Known)> RecallsAsync(
        CancellationToken cancellationToken)
    {
        var recalls = new List<DomainFact>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var fact in this.store
            .TimelineAsync(DOMAIN, RECALL_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            if (fact.Category == MATCH_CATEGORY)
            {
                known.Add(fact.Description);
            }
            else
            {
                recalls.Add(fact);
            }
        }

        return (recalls, known);
    }
}
