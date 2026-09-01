using System.Globalization;
using Dami.Contracts.Domains;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Recalls;

/// <summary>Pulls public recall feeds by date window and records them (H12, egress half).</summary>
/// <remarks>
/// This half holds the egress client and therefore, by the recorded D-012 rule, never
/// touches health data: the queries carry a date window and nothing else, FDA rows are
/// recorded for the local-only matcher to judge, and the only surfacing this half may
/// produce is a CPSC hit on the configured household terms — which are gear, not health.
/// </remarks>
public sealed class RecallCollectorService : IProactiveService
{
    private const string DOMAIN = "recall";
    private const int KNOWN_LIMIT = 800;
    private const int MAX_SURFACINGS_PER_PASS = 3;

    private readonly IDomainFactStore store;
    private readonly IEgressClient egressClient;
    private readonly RecallSentinelOptions sentinelOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<RecallCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public RecallCollectorService(
        IDomainFactStore store,
        IEgressClient egressClient,
        IOptions<RecallSentinelOptions> sentinelOptions,
        TimeProvider clock,
        ILogger<RecallCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(sentinelOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.sentinelOptions = sentinelOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "recall-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var known = await this.KnownAsync(cancellationToken).ConfigureAwait(false);
        var surfacings = new List<Surfacing>();
        var written = 0;
        foreach (var (url, source) in this.Sources())
        {
            written += await this.ReadOneAsync(url, source, known, surfacings, context, cancellationToken)
                .ConfigureAwait(false);
        }

        this.logger.LogInformation(
            "Recall collector: {Written} new notice(s), {Surfaced} surfaced", written, surfacings.Count);
        return surfacings.Count == 0
            ? ProactiveResult.Did($"{written} new recall notice(s)")
            : new ProactiveResult(
                Array.Empty<Conclusion>(), surfacings, ProactiveStatus.Completed,
                $"{written} new recall notice(s), {surfacings.Count} surfaced");
    }

    private List<(string Url, string Source)> Sources()
    {
        var now = this.clock.GetUtcNow();
        var from = now.AddDays(-Math.Max(1, this.sentinelOptions.LookbackDays));
        return
        [
            (Fill(this.sentinelOptions.DrugUrl, from, now, "yyyyMMdd"), "drug"),
            (Fill(this.sentinelOptions.DeviceUrl, from, now, "yyyyMMdd"), "device"),
            (Fill(this.sentinelOptions.CpscUrl, from, now, "yyyy-MM-dd"), "cpsc"),
        ];
    }

    private static string Fill(string template, DateTimeOffset from, DateTimeOffset to, string format) =>
        string.Format(
            CultureInfo.InvariantCulture, template,
            from.ToString(format, CultureInfo.InvariantCulture),
            to.ToString(format, CultureInfo.InvariantCulture));

    private async Task<int> ReadOneAsync(
        string url,
        string source,
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RecallNotice> notices;
        try
        {
            var response = await this.egressClient.SendAsync(
                new EgressRequest(
                    new Uri(url), $"recall feed {source}", context.TraceId,
                    ExecutionOrigin.ScheduledService),
                cancellationToken).ConfigureAwait(false);
            notices = source == "cpsc"
                ? RecallFeeds.ParseCpsc(response.Body)
                : RecallFeeds.ParseOpenFda(response.Body, source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // CPSC in particular goes dark for days at a time; one dead agency must
            // not silence the others.
            this.logger.LogWarning(exception, "Recall source {Source} failed; continuing", source);
            return 0;
        }

        return await this.RecordAsync(notices, known, surfacings, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> RecordAsync(
        IReadOnlyList<RecallNotice> notices,
        HashSet<string> known,
        List<Surfacing> surfacings,
        CancellationToken cancellationToken)
    {
        var written = 0;
        foreach (var notice in notices)
        {
            var household = notice.Source == "cpsc"
                ? RecallTerms.Mentions(notice.Product, this.sentinelOptions.HouseholdTerms)
                : null;
            if (!Keep(notice, household) || known.Contains(Description(notice)))
            {
                continue;
            }

            var recorded = await this.store.RecordAsync(
                new DomainFact(
                    Guid.NewGuid(), DOMAIN,
                    notice.Date ?? DateOnly.FromDateTime(this.clock.GetUtcNow().UtcDateTime),
                    notice.Source, Description(notice), this.ServiceName, this.clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
            if (recorded)
            {
                written++;
                known.Add(Description(notice));
                this.SurfaceHousehold(surfacings, notice, household);
            }
        }

        return written;
    }

    /// <summary>Class III is labeling noise; CPSC rows only matter when gear matches.</summary>
    private static bool Keep(RecallNotice notice, string? household) =>
        notice.Source == "cpsc"
            ? household is not null
            : notice.Classification is "Class I" or "Class II";

    private void SurfaceHousehold(List<Surfacing> surfacings, RecallNotice notice, string? household)
    {
        if (household is not null && surfacings.Count < MAX_SURFACINGS_PER_PASS)
        {
            surfacings.Add(new Surfacing(
                Guid.NewGuid(), this.ServiceName,
                notice.Product,
                $"Matches your '{household}'. {notice.Reason} {notice.Reference}",
                this.sentinelOptions.Confidence, this.clock.GetUtcNow()));
        }
    }

    private static string Description(RecallNotice notice) =>
        $"[{notice.Source} {notice.Classification}] {notice.Product} — {notice.Reason} ({notice.Reference})";

    private async Task<HashSet<string>> KnownAsync(CancellationToken cancellationToken)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var fact in this.store
            .TimelineAsync(DOMAIN, KNOWN_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            // Match rows are the local-only half's output and carry drug names drawn from
            // the health record. This half holds the egress client and must not read them,
            // and they would also consume the dedup window meant for notices.
            if (fact.Category != "match")
            {
                known.Add(fact.Description);
            }
        }

        return known;
    }
}
