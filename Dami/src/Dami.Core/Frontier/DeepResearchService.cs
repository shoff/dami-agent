using System.Net;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>Traverses relevant references behind the public-only research reader.</summary>
public sealed class DeepResearchService : IDeepResearchService
{
    private const int HARD_MAX_PAGES = 25;
    private const int HARD_MAX_DEPTH = 4;
    private const int HARD_MAX_REFERENCES_PER_PAGE = 20;

    private static readonly HashSet<string> assetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".ico", ".svg", ".jpg", ".jpeg", ".gif", ".webp", ".avif",
        ".css", ".js", ".woff", ".woff2", ".ttf", ".eot",
    };

    private readonly IResearchReader reader;
    private readonly DeepResearchOptions researchOptions;
    private readonly ILogger<DeepResearchService> logger;
    private readonly ResearchJournal journal;

    /// <summary>Creates a bounded traversal service.</summary>
    public DeepResearchService(
        IResearchReader reader,
        IOptions<DeepResearchOptions> researchOptions,
        ILogger<DeepResearchService> logger,
        ResearchJournal journal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(researchOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(journal);
        this.reader = reader;
        this.researchOptions = researchOptions.Value;
        this.logger = logger;
        this.journal = journal;
    }

    /// <inheritdoc />
    public Task<DeepResearchResult> ExploreAsync(Uri seed, string question, Guid traceId,
        ExecutionOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return this.ExploreCoreAsync(new[] { seed }, question, traceId, origin, false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DeepResearchResult> ExploreAsync(IReadOnlyList<Uri> seeds, string question, Guid traceId,
        ExecutionOrigin origin, CancellationToken cancellationToken) =>
        this.ExploreCoreAsync(seeds, question, traceId, origin, true, cancellationToken);

    private async Task<DeepResearchResult> ExploreCoreAsync(IReadOnlyList<Uri> seeds, string question, Guid traceId,
        ExecutionOrigin origin, bool automaticSources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        if (seeds.Count == 0) { throw new ArgumentException("At least one starting URL is required.", nameof(seeds)); }
        seeds = seeds.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Select(url => new Uri(url)).ToArray();
        var progress = await this.journal.BeginAsync(seeds, question, traceId, origin, automaticSources, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await this.TraverseAsync(seeds, question, traceId, origin, progress, cancellationToken).ConfigureAwait(false);
            await progress.CompletedAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            await progress.StoppedAsync(exception).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<DeepResearchResult> TraverseAsync(IReadOnlyList<Uri> seeds, string question, Guid traceId,
        ExecutionOrigin origin, ResearchProgress progress, CancellationToken cancellationToken)
    {
        var terms = Terms(question);
        var candidates = seeds.Select((seed, index) => new Candidate(seed, 0, [seed], int.MaxValue, index)).ToList();
        var scheduled = seeds.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var findings = new List<ResearchFinding>();
        var attempts = 0;
        var references = 0;
        var order = seeds.Count;
        while (candidates.Count > 0 && attempts < this.PageLimit)
        {
            var candidate = TakeBest(candidates);
            attempts++;
            await progress.ReadingAsync(candidate.Url, cancellationToken).ConfigureAwait(false);
            var page = await this.TryReadAsync(candidate, traceId, origin, progress, cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                continue;
            }

            findings.Add(new ResearchFinding(page, candidate.Depth, candidate.Path));
            references += this.Schedule(page, candidate, terms, candidates, scheduled, ref order);
            await progress.FoundAsync(findings[^1], references, cancellationToken).ConfigureAwait(false);
        }

        return new DeepResearchResult(findings, attempts, references, candidates.Count > 0) { Issues = progress.Issues };
    }

    private async Task<ResearchPage?> TryReadAsync(
        Candidate candidate,
        Guid traceId,
        ExecutionOrigin origin,
        ResearchProgress progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await this.reader.ReadAsync(candidate.Url, traceId, origin, cancellationToken).ConfigureAwait(false);
            if (page.StatusCode is < 200 or >= 300)
            {
                throw new HttpRequestException($"Source returned HTTP {page.StatusCode}.");
            }

            return page;
        }
        catch (Exception exception) when (exception is EgressRefusedException or HttpRequestException or IOException or TimeoutException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            this.logger.LogWarning("Deep research skipped {Url}: {Reason}", candidate.Url, exception.Message);
            await progress.SkippedAsync(candidate.Url, exception.Message, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private int Schedule(
        ResearchPage page,
        Candidate parent,
        IReadOnlySet<string> terms,
        List<Candidate> candidates,
        HashSet<string> scheduled,
        ref int order)
    {
        if (parent.Depth >= this.DepthLimit)
        {
            return page.References.Count;
        }

        var ranked = page.References
            .Select(reference => (Reference: reference, Score: Score(reference, terms)))
            .Where(item => item.Score > 0 && IsCandidate(item.Reference.Url))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Reference.Url.AbsoluteUri, StringComparer.Ordinal)
            .Take(this.ReferenceLimit);
        foreach (var item in ranked)
        {
            var key = Key(item.Reference.Url);
            if (scheduled.Add(key))
            {
                candidates.Add(new Candidate(
                    new Uri(key), parent.Depth + 1, [.. parent.Path, new Uri(key)], item.Score, order++));
            }
        }

        return page.References.Count;
    }

    private static Candidate TakeBest(List<Candidate> candidates)
    {
        var best = candidates
            .Select((candidate, index) => (Candidate: candidate, Index: index))
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Order)
            .First();
        candidates.RemoveAt(best.Index);
        return best.Candidate;
    }

    private static int Score(ResearchReference reference, IReadOnlySet<string> terms)
    {
        var haystack = reference.Label + " " + reference.Url.Host + " " + reference.Url.AbsolutePath;
        var matches = terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
        var kind = reference.Kind switch
        {
            ResearchReferenceKind.Data => 6,
            ResearchReferenceKind.Api => 5,
            ResearchReferenceKind.Feed => 4,
            _ => 0,
        };
        return (matches * 2) + kind;
    }

    private static IReadOnlySet<string> Terms(string question) => question
        .Split([' ', '\t', '\r', '\n', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
        .Select(term => term.Trim('.', ',', ':', ';', '?', '!', '(', ')').ToLowerInvariant())
        .Where(term => term.Length >= 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsCandidate(Uri address)
    {
        if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (assetExtensions.Contains(Path.GetExtension(address.AbsolutePath)))
        {
            return false;
        }

        if (address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(address.Host, out var literal) || IsPublicLiteral(literal);
    }

    private static bool IsPublicLiteral(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127));
    }

    private static string Key(Uri address) => new UriBuilder(address) { Fragment = string.Empty }.Uri.AbsoluteUri;

    private int PageLimit => Math.Clamp(this.researchOptions.MaxPages, 1, HARD_MAX_PAGES);

    private int DepthLimit => Math.Clamp(this.researchOptions.MaxDepth, 0, HARD_MAX_DEPTH);

    private int ReferenceLimit => Math.Clamp(
        this.researchOptions.MaxReferencesPerPage, 1, HARD_MAX_REFERENCES_PER_PAGE);

    private sealed record Candidate(Uri Url, int Depth, IReadOnlyList<Uri> Path, int Score, int Order);
}

/// <summary>A dedicated per-run traversal budget for deep research.</summary>
public sealed class DeepResearchOptions
{
    /// <summary>Configuration section shared with the other research controls.</summary>
    public const string SECTION_NAME = "Research";

    /// <summary>Most page reads attempted in one deep-research run.</summary>
    public int MaxPages { get; set; } = 8;

    /// <summary>Most reference edges followed from the seed.</summary>
    public int MaxDepth { get; set; } = 2;

    /// <summary>Most relevant references scheduled from one page.</summary>
    public int MaxReferencesPerPage { get; set; } = 12;
}
