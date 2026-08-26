using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Dami.Contracts.Domains;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Scout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Civic;

/// <summary>The civic domain from public feeds (K4): notices and meetings, as dated facts.</summary>
/// <remarks>
/// The only domain collector that leaves the host, and it leaves carrying nothing: a GET
/// of a public feed through the <see cref="IEgressClient"/> boundary, which allowlists the
/// host and records the send. Items become one fact each, deduplicated per day, so a
/// re-read of the same feed writes nothing new. It surfaces nothing itself; the facts join
/// retrieval and reflection like every other domain's.
/// </remarks>
public sealed partial class CivicFeedCollectorService : IProactiveService
{
    private const string DOMAIN = "civic";

    private readonly IDomainFactStore store;
    private readonly IEgressClient egressClient;
    private readonly CivicFeedOptions feedOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<CivicFeedCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public CivicFeedCollectorService(
        IDomainFactStore store,
        IEgressClient egressClient,
        IOptions<CivicFeedOptions> feedOptions,
        TimeProvider clock,
        ILogger<CivicFeedCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(feedOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.feedOptions = feedOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "civic-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var delay = TimeSpan.FromSeconds(Math.Max(0, this.feedOptions.FeedDelaySeconds));
        var written = 0;
        for (var index = 0; index < this.feedOptions.Feeds.Count; index++)
        {
            if (index > 0 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, this.clock, cancellationToken).ConfigureAwait(false);
            }

            written += await this.ReadOneAsync(this.feedOptions.Feeds[index], context, cancellationToken)
                .ConfigureAwait(false);
        }

        this.logger.LogInformation("Civic collector: {Written} new fact(s) from {Feeds} feed(s)", written, this.feedOptions.Feeds.Count);
        return ProactiveResult.quiet;
    }

    /// <summary>The CivicPlus "Event date:" line, when the description carries one.</summary>
    internal static DateOnly? EventDate(string? summary)
    {
        if (summary is null)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(TagPattern().Replace(summary, " "));
        var match = EventDatePattern().Match(text);
        return match.Success
            && DateOnly.TryParseExact(match.Groups[1].Value.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"Event date:\s*([A-Za-z]+ \d{1,2}, \d{4})")]
    private static partial Regex EventDatePattern();

    private async Task<int> ReadOneAsync(CivicFeed feed, ProactiveContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<FeedItem> items;
        try
        {
            var response = await this.egressClient.SendAsync(
                new EgressRequest(new Uri(feed.Url), $"civic feed {feed.Name}", context.TraceId, ExecutionOrigin.ScheduledService),
                cancellationToken).ConfigureAwait(false);
            items = FeedParser.Parse(response.Body);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One dead or refused feed must not silence the rest of the pass.
            this.logger.LogWarning(exception, "Civic feed {Feed} failed; continuing", feed.Name);
            return 0;
        }

        var now = this.clock.GetUtcNow();
        var written = 0;
        foreach (var item in items)
        {
            // A calendar item's pubDate is when it was posted; the day it happens is in the
            // description ("Event date: August 26, 2026"). That is the fact's date.
            var asOf = EventDate(item.Summary) ?? DateOnly.FromDateTime((item.PublishedAt ?? now).UtcDateTime);
            var fact = new DomainFact(
                Guid.NewGuid(), DOMAIN, asOf, feed.Category, $"{item.Title.Trim()} — {item.Link}", feed.Name, now);
            written += await this.store.RecordAsync(fact, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return written;
    }
}
