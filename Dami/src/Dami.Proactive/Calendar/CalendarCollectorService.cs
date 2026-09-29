using Dami.Contracts.Calendar;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Calendar;

/// <summary>Mirrors Steve's calendar for today and the next two weeks (docs/agent-landscape-2026-09.md D1).</summary>
/// <remarks>
/// Calendar was the single most-used integration in the 2026-09-29 survey. Read-only at
/// the source; the request carries nothing of Steve's beyond the secret address itself, and
/// the egress door logs only the host. A body that is not a calendar fails the pass and
/// leaves the old mirror alone: an empty "today" must mean an empty day.
/// </remarks>
public sealed class CalendarCollectorService : IProactiveService
{
    private readonly ICalendarStore store;
    private readonly IEgressClient egressClient;
    private readonly CalendarOptions calendarOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<CalendarCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public CalendarCollectorService(
        ICalendarStore store,
        IEgressClient egressClient,
        IOptions<CalendarOptions> calendarOptions,
        TimeProvider clock,
        ILogger<CalendarCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(calendarOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.calendarOptions = calendarOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "calendar-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.EightHourly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (this.calendarOptions.IcsUrl.Length == 0)
        {
            return ProactiveResult.Did("no calendar configured (Calendar__IcsUrl)");
        }

        var response = await this.egressClient.SendAsync(
            new EgressRequest(new Uri(this.calendarOptions.IcsUrl), "calendar read", context.TraceId, ExecutionOrigin.ScheduledService),
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != 200 || !response.Body.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal))
        {
            return new ProactiveResult(
                [], [], ProactiveStatus.Failed,
                $"the calendar answered {response.StatusCode} without a calendar; was the secret address reset?");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(this.calendarOptions.TimeZone);
        var today = TimeZoneInfo.ConvertTime(this.clock.GetUtcNow(), zone).Date;
        var from = new DateTimeOffset(today, zone.GetUtcOffset(today));
        var to = from.AddDays(this.calendarOptions.DaysAhead + 1);
        var events = CalendarReader.Read(response.Body, from, to, zone);
        await this.store.ReplaceAsync(from, to, events, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Calendar mirrored: {Count} occurrence(s) over {Days} days", events.Count, this.calendarOptions.DaysAhead + 1);
        return ProactiveResult.Did($"{events.Count} calendar occurrence(s) mirrored");
    }
}
