using Dami.Contracts.Domains;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Scout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Releases;

/// <summary>Watches public release sources for fixes to the software this host runs (H13).</summary>
/// <remarks>
/// Egress carries nothing but a GET of a public URL; what this host runs — the baselines —
/// stays in configuration and is only ever compared locally. A release surfaces once: the
/// domain timeline is the memory, so a nightly re-read of the same feed writes nothing and
/// says nothing. A watch with no baseline learns the world silently on first sight and
/// speaks only about change after that (D-021 — the ordinary night produces nothing).
/// </remarks>
public sealed class ReleaseWatchService : IProactiveService
{
    private const string DOMAIN = "release";
    private const int KNOWN_LIMIT = 500;
    private const int MAX_ENTRIES_PER_FEED = 5;
    private const int MAX_SURFACINGS_PER_PASS = 3;

    private readonly IDomainFactStore store;
    private readonly IEgressClient egressClient;
    private readonly ReleaseWatchOptions watchOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ReleaseWatchService> logger;

    /// <summary>Creates the service.</summary>
    public ReleaseWatchService(
        IDomainFactStore store,
        IEgressClient egressClient,
        IOptions<ReleaseWatchOptions> watchOptions,
        TimeProvider clock,
        ILogger<ReleaseWatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(watchOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.watchOptions = watchOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "release-watch";

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
        var delay = TimeSpan.FromSeconds(Math.Max(0, this.watchOptions.WatchDelaySeconds));
        for (var index = 0; index < this.watchOptions.Watches.Count; index++)
        {
            if (index > 0 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, this.clock, cancellationToken).ConfigureAwait(false);
            }

            written += await this.WatchOneAsync(
                this.watchOptions.Watches[index], known, surfacings, context, cancellationToken)
                .ConfigureAwait(false);
        }

        this.logger.LogInformation(
            "Release watch: {Written} new fact(s), {Surfaced} surfaced", written, surfacings.Count);
        return surfacings.Count == 0
            ? ProactiveResult.Did($"{written} new release fact(s)")
            : new ProactiveResult(
                Array.Empty<Conclusion>(), surfacings, ProactiveStatus.Completed,
                $"{written} new release fact(s), {surfacings.Count} surfaced");
    }

    private async Task<int> WatchOneAsync(
        ReleaseWatch watch,
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        List<(string Version, string Link, DateOnly AsOf)> candidates;
        try
        {
            var response = await this.egressClient.SendAsync(
                new EgressRequest(
                    new Uri(watch.Url), $"release watch {watch.Name}",
                    context.TraceId, ExecutionOrigin.ScheduledService),
                cancellationToken).ConfigureAwait(false);
            candidates = Candidates(watch, response.Body, this.clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One refused or dead source must not silence the rest of the pass.
            this.logger.LogWarning(exception, "Release watch {Watch} failed; continuing", watch.Name);
            return 0;
        }

        return await this.RecordAsync(watch, candidates, known, surfacings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<(string Version, string Link, DateOnly AsOf)> Candidates(
        ReleaseWatch watch, string body, DateTimeOffset now)
    {
        var results = new List<(string, string, DateOnly)>();
        if (watch.Kind == "nvidia-latest")
        {
            // latest.txt is one line: the version, then the path. No date rides along.
            var version = ReleaseVersions.Extract(body);
            if (version is not null)
            {
                results.Add((version, watch.Url, DateOnly.FromDateTime(now.UtcDateTime)));
            }

            return results;
        }

        var items = FeedParser.Parse(body);
        for (var index = 0; index < items.Count && results.Count < MAX_ENTRIES_PER_FEED; index++)
        {
            var version = ReleaseVersions.Extract(items[index].Title);
            if (version is not null && !IsPreRelease(items[index].Title))
            {
                var at = items[index].PublishedAt ?? now;
                results.Add((version, items[index].Link, DateOnly.FromDateTime(at.UtcDateTime)));
            }
        }

        return results;
    }

    /// <summary>An rc, beta, or preview is not a fix anyone installs from a watch.</summary>
    private static bool IsPreRelease(string title) =>
        title.Contains("-rc", StringComparison.OrdinalIgnoreCase)
            || title.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || title.Contains("preview", StringComparison.OrdinalIgnoreCase)
            || title.Contains("alpha", StringComparison.OrdinalIgnoreCase);

    private async Task<int> RecordAsync(
        ReleaseWatch watch,
        List<(string Version, string Link, DateOnly AsOf)> candidates,
        HashSet<string> known,
        List<Surfacing> surfacings,
        CancellationToken cancellationToken)
    {
        var learned = HasWatch(known, watch.Name);
        var written = 0;
        foreach (var (version, link, asOf) in candidates)
        {
            var description = $"{watch.Name} {version} — {link}";
            var news = watch.Baseline.Length == 0
                ? learned
                : ReleaseVersions.IsNewer(version, watch.Baseline);
            if (known.Contains(description) || (watch.Baseline.Length > 0 && !news))
            {
                // Already on record, or history at-or-below what already runs here.
                continue;
            }

            if (!await this.RecordFactAsync(asOf, description, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            written++;
            known.Add(description);
            if (news && surfacings.Count < MAX_SURFACINGS_PER_PASS)
            {
                surfacings.Add(this.Surface(watch, version, link));
            }
        }

        return written;
    }

    private Task<bool> RecordFactAsync(DateOnly asOf, string description, CancellationToken cancellationToken) =>
        this.store.RecordAsync(
            new DomainFact(
                Guid.NewGuid(), DOMAIN, asOf, "release", description,
                this.ServiceName, this.clock.GetUtcNow()),
            cancellationToken);

    private Surfacing Surface(ReleaseWatch watch, string version, string link)
    {
        var title = watch.Baseline.Length > 0
            ? $"{watch.Name} {version} is out (you run {watch.Baseline})"
            : $"{watch.Name} {version} is out";
        return new Surfacing(
            Guid.NewGuid(), this.ServiceName, title,
            watch.Reason.Length > 0 ? $"{watch.Reason}. {link}" : link,
            this.watchOptions.Confidence, this.clock.GetUtcNow());
    }

    private async Task<HashSet<string>> KnownAsync(CancellationToken cancellationToken)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var fact in this.store
            .TimelineAsync(DOMAIN, KNOWN_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            known.Add(fact.Description);
        }

        return known;
    }

    private static bool HasWatch(HashSet<string> known, string name)
    {
        foreach (var description in known)
        {
            if (description.StartsWith(name + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
