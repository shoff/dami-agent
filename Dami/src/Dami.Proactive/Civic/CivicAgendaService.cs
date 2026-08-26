using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive.Civic;

/// <summary>Once a week, what is on the civic calendar for the next seven days (K4, H5).</summary>
/// <remarks>
/// The civic collector writes meetings as facts; this turns the coming week's into one
/// surfacing, so `dami inbox` carries "Lakeville, week of …" once and not every night.
/// The title names the week, and a week already surfaced is recognised in the queue's
/// recent rows, which is the only memory this needs. Nothing leaves the host.
/// </remarks>
public sealed class CivicAgendaService : IProactiveService
{
    private const int LOOKAHEAD_DAYS = 7;
    private const int RECENT_TO_CHECK = 100;
    private const double CONFIDENCE = 0.6;

    private readonly IDomainFactStore store;
    private readonly ISurfacingQueue queue;
    private readonly TimeProvider clock;
    private readonly ILogger<CivicAgendaService> logger;

    /// <summary>Creates the service.</summary>
    public CivicAgendaService(
        IDomainFactStore store,
        ISurfacingQueue queue,
        TimeProvider clock,
        ILogger<CivicAgendaService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.queue = queue;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "civic-agenda";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = this.clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var title = $"Civic calendar, week of {today:yyyy-MM-dd}";
        if (await this.AlreadySurfacedAsync(title, cancellationToken).ConfigureAwait(false))
        {
            return ProactiveResult.quiet;
        }

        var meetings = new List<DomainFact>();
        await foreach (var fact in this.store
            .BetweenAsync("civic", today, today.AddDays(LOOKAHEAD_DAYS), 50, cancellationToken).ConfigureAwait(false))
        {
            if (fact.Category == "meeting")
            {
                meetings.Add(fact);
            }
        }

        if (meetings.Count == 0)
        {
            return ProactiveResult.quiet;
        }

        var body = string.Join('\n', meetings.Select(fact => $"{fact.AsOf:ddd yyyy-MM-dd}  {fact.Description}"));
        this.logger.LogInformation("Civic agenda: {Count} meeting(s) in the next {Days} days", meetings.Count, LOOKAHEAD_DAYS);
        return new ProactiveResult(
            [], [new Surfacing(Guid.NewGuid(), this.ServiceName, $"{title}: {meetings.Count} meeting(s)", body, CONFIDENCE, now)],
            ProactiveStatus.Completed);
    }

    /// <summary>The week is surfaced once; nightly passes within it find it in the recent rows.</summary>
    private async Task<bool> AlreadySurfacedAsync(string title, CancellationToken cancellationToken)
    {
        await foreach (var recent in this.queue.RecentAsync(RECENT_TO_CHECK, cancellationToken).ConfigureAwait(false))
        {
            if (recent.ServiceName == this.ServiceName
                && recent.Title.StartsWith(title, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
