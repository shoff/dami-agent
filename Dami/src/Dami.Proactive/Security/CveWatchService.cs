using System.Globalization;
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
/// The join is the privacy design, and both halves of it pull broad and match at home:
/// the USN feed comes down whole, and the advisory feed comes down by publication date.
/// Neither query says anything about this machine.
///
/// An earlier version passed the resolved NuGet closure as `affects=` — public package
/// names, no versions, but in aggregate the dependency manifest of a private repository,
/// sent nightly in stable order. An audit called that a durable fingerprint and it was
/// right. Pulling recent advisories and filtering locally costs a few more rows over the
/// wire and discloses nothing, which is the trade every other collector here already
/// makes (the recall sentinel pulls openFDA wholesale with no query at all).
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
            for (var page = 1; page <= Math.Max(1, this.cveOptions.MaxPages); page++)
            {
                written += await this.QueryPageAsync(
                    packages, page, known, surfacings, context, cancellationToken)
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

    /// <summary>One page of recent advisories, matched locally against the closure.</summary>
    private async Task<int> QueryPageAsync(
        IReadOnlyList<(string Name, string Version)> packages,
        int page,
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        // ecosystem and a publication window only. Nothing here names anything installed.
        var since = this.clock.GetUtcNow()
            .AddDays(-Math.Max(1, this.cveOptions.LookbackDays))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"{this.cveOptions.AdvisoriesUrl}?ecosystem=nuget&published=%3E{since}"
            + $"&per_page={this.cveOptions.PageSize}&page={page}";
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
