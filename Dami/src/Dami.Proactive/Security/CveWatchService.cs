using Dami.Contracts.Domains;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Scout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Security;

/// <summary>Joins public vulnerability data against what this host runs (H11).</summary>
/// <remarks>
/// The join is the privacy design: the USN feed comes down whole with no query at all,
/// and the advisories query carries public OSS package names — never versions, never
/// anything about this host. What is installed is read locally and stays local; a fetch
/// with no local match records nothing and says nothing (D-021). An advisory surfaces
/// once — the security domain timeline is the memory.
/// </remarks>
public sealed class CveWatchService : IProactiveService
{
    private const string DOMAIN = "security";
    private const int KNOWN_LIMIT = 500;
    private const int MAX_SURFACINGS_PER_PASS = 3;

    private readonly IDomainFactStore store;
    private readonly IEgressClient egressClient;
    private readonly IInstalledInventory inventory;
    private readonly CveWatchOptions cveOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<CveWatchService> logger;

    /// <summary>Creates the service.</summary>
    public CveWatchService(
        IDomainFactStore store,
        IEgressClient egressClient,
        IInstalledInventory inventory,
        IOptions<CveWatchOptions> cveOptions,
        TimeProvider clock,
        ILogger<CveWatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(cveOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.inventory = inventory;
        this.cveOptions = cveOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "cve-watch";

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
        written += await this.UsnPassAsync(known, surfacings, context, cancellationToken)
            .ConfigureAwait(false);
        written += await this.NugetPassAsync(known, surfacings, context, cancellationToken)
            .ConfigureAwait(false);

        this.logger.LogInformation(
            "CVE watch: {Written} new fact(s), {Surfaced} surfaced", written, surfacings.Count);
        return surfacings.Count == 0
            ? ProactiveResult.Did($"{written} new security fact(s)")
            : new ProactiveResult(
                Array.Empty<Conclusion>(), surfacings, ProactiveStatus.Completed,
                $"{written} new security fact(s), {surfacings.Count} surfaced");
    }

    private async Task<int> UsnPassAsync(
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var packages = await this.inventory.SystemPackagesAsync(cancellationToken)
                .ConfigureAwait(false);
            var names = new List<string>(packages.Count);
            foreach (var (name, _) in packages)
            {
                names.Add(name);
            }

            var response = await this.FetchAsync(this.cveOptions.UsnFeedUrl, "USN feed", context, cancellationToken)
                .ConfigureAwait(false);
            return await this.MatchUsnAsync(
                FeedParser.Parse(response.Body), names, known, surfacings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "USN pass failed; continuing");
            return 0;
        }
    }

    private async Task<int> MatchUsnAsync(
        IReadOnlyList<FeedItem> items,
        List<string> installed,
        HashSet<string> known,
        List<Surfacing> surfacings,
        CancellationToken cancellationToken)
    {
        var written = 0;
        foreach (var item in items)
        {
            var match = SecurityAdvisories.UsnMentions(item.Title, installed);
            if (match is null)
            {
                continue;
            }

            var description = $"{item.Title} — affects {match} — {item.Link}";
            var asOf = DateOnly.FromDateTime((item.PublishedAt ?? this.clock.GetUtcNow()).UtcDateTime);
            if (!await this.RecordAsync(known, "usn", asOf, description, cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            written++;
            this.TrySurface(surfacings, item.Title, $"Affects installed {match}. {item.Link}");
        }

        return written;
    }

    private async Task<int> NugetPassAsync(
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var packages = await this.inventory.NugetPackagesAsync(cancellationToken)
                .ConfigureAwait(false);
            var written = 0;
            for (var start = 0; start < packages.Count; start += Math.Max(1, this.cveOptions.QueryChunk))
            {
                written += await this.QueryChunkAsync(
                    packages, start, known, surfacings, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            return written;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "NuGet advisories pass failed; continuing");
            return 0;
        }
    }

    private async Task<int> QueryChunkAsync(
        IReadOnlyList<(string Name, string Version)> packages,
        int start,
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        for (var index = start; index < packages.Count && names.Count < Math.Max(1, this.cveOptions.QueryChunk); index++)
        {
            names.Add(Uri.EscapeDataString(packages[index].Name));
        }

        var url = $"{this.cveOptions.AdvisoriesUrl}?ecosystem=nuget&affects={string.Join(",", names)}";
        var response = await this.FetchAsync(url, "NuGet advisories", context, cancellationToken)
            .ConfigureAwait(false);

        var written = 0;
        foreach (var advisory in SecurityAdvisories.ParseGithub(response.Body))
        {
            written += await this.MatchAdvisoryAsync(advisory, packages, known, surfacings, cancellationToken)
                .ConfigureAwait(false)
                ? 1
                : 0;
        }

        return written;
    }

    private async Task<bool> MatchAdvisoryAsync(
        NugetAdvisory advisory,
        IReadOnlyList<(string Name, string Version)> packages,
        HashSet<string> known,
        List<Surfacing> surfacings,
        CancellationToken cancellationToken)
    {
        var version = InstalledVersion(packages, advisory.Package);
        if (version is null
            || advisory.Range.Length == 0
            || !VersionRanges.Matches(version, advisory.Range))
        {
            return false;
        }

        var description =
            $"{advisory.GhsaId}: {advisory.Package} {version} vulnerable ({advisory.Range}) — {advisory.Url}";
        var asOf = DateOnly.FromDateTime((advisory.PublishedAt ?? this.clock.GetUtcNow()).UtcDateTime);
        if (!await this.RecordAsync(known, "nuget", asOf, description, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        this.TrySurface(
            surfacings,
            $"{advisory.Package} {version}: {advisory.Summary}",
            $"{advisory.GhsaId} ({advisory.Severity}) — vulnerable {advisory.Range}. {advisory.Url}");
        return true;
    }

    private async Task<EgressResponse> FetchAsync(
        string url, string purpose, ProactiveContext context, CancellationToken cancellationToken)
    {
        return await this.egressClient.SendAsync(
            new EgressRequest(new Uri(url), purpose, context.TraceId, ExecutionOrigin.ScheduledService),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RecordAsync(
        HashSet<string> known,
        string category,
        DateOnly asOf,
        string description,
        CancellationToken cancellationToken)
    {
        if (known.Contains(description))
        {
            return false;
        }

        var recorded = await this.store.RecordAsync(
            new DomainFact(
                Guid.NewGuid(), DOMAIN, asOf, category, description,
                this.ServiceName, this.clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (recorded)
        {
            known.Add(description);
        }

        return recorded;
    }

    private void TrySurface(List<Surfacing> surfacings, string title, string body)
    {
        if (surfacings.Count < MAX_SURFACINGS_PER_PASS)
        {
            surfacings.Add(new Surfacing(
                Guid.NewGuid(), this.ServiceName, title, body,
                this.cveOptions.Confidence, this.clock.GetUtcNow()));
        }
    }

    private static string? InstalledVersion(
        IReadOnlyList<(string Name, string Version)> packages, string name)
    {
        foreach (var (candidate, version) in packages)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }

        return null;
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
}
