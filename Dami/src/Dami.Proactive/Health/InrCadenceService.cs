using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Health;

/// <summary>
/// A heads-up, from the health timeline alone, when the interval Steve has kept between
/// recorded INR checks lapses. Local-only; propose-only; once per lapse.
/// </summary>
/// <remarks>
/// The interval is his own, measured from the checks he has recorded with a value ("INR
/// 2.4"), never a clinical rule — Dami does not know his target range or his clinic's
/// schedule and does not pretend to. Mentions without a value ("INR monitored") are not
/// checks. It says what the log shows and invites the correction, in Oura's forgiven
/// register: a heads-up that can be wrong, never a grade and never a nag. On 2026-09-16
/// the timeline held no valued check at all, so this stays quiet until one is recorded.
/// </remarks>
public sealed partial class InrCadenceService : IProactiveService
{
    private readonly IHealthEventStore healthStore;
    private readonly ISurfacingQueue surfacings;
    private readonly InrCadenceOptions cadenceOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<InrCadenceService> logger;

    /// <summary>Creates the service.</summary>
    public InrCadenceService(
        IHealthEventStore healthStore,
        ISurfacingQueue surfacings,
        IOptions<InrCadenceOptions> cadenceOptions,
        TimeProvider clock,
        ILogger<InrCadenceService> logger)
    {
        ArgumentNullException.ThrowIfNull(healthStore);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(cadenceOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.healthStore = healthStore;
        this.surfacings = surfacings;
        this.cadenceOptions = cadenceOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "inr-cadence";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var checks = await this.ChecksAsync(cancellationToken).ConfigureAwait(false);
        if (checks.Count < this.cadenceOptions.MinimumChecks)
        {
            this.logger.LogInformation("INR cadence: {Count} valued check(s) on the timeline; below {Floor}, staying quiet", checks.Count, this.cadenceOptions.MinimumChecks);
            return ProactiveResult.quiet;
        }

        var last = checks[^1];
        var interval = this.KeptInterval(checks);
        var today = this.Today();
        var elapsed = today.DayNumber - last.Day.DayNumber;
        var lapse = (int)Math.Ceiling(interval * this.cadenceOptions.LapseFactor);
        if (elapsed < lapse || await this.SaidSinceAsync(last.Day, cancellationToken).ConfigureAwait(false))
        {
            return ProactiveResult.quiet;
        }

        this.logger.LogInformation("INR cadence: {Elapsed} days since the last valued check against a kept interval of {Interval}", elapsed, interval);
        return new ProactiveResult(
            Array.Empty<Conclusion>(), [this.Surface(last, checks.Count, interval, elapsed)], ProactiveStatus.Completed,
            $"{elapsed} days since {last.Day:yyyy-MM-dd}");
    }

    /// <summary>Vital events carrying "INR" and a number, one per day, oldest first.</summary>
    private async Task<List<Check>> ChecksAsync(CancellationToken cancellationToken)
    {
        var byDay = new SortedDictionary<DateOnly, Check>();
        await foreach (var item in this.healthStore.TimelineAsync(this.cadenceOptions.TimelineRows, cancellationToken)
            .ConfigureAwait(false))
        {
            if (item.Category != HealthCategory.Vital || item.EventDate.Year < 1971)
            {
                continue;
            }

            var match = ValuedInr().Match(item.Description);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                byDay[item.EventDate] = new Check(item.EventDate, value);
            }
        }

        return [.. byDay.Values];
    }

    private double KeptInterval(List<Check> checks)
    {
        var gaps = new List<double>(checks.Count - 1);
        for (var index = 1; index < checks.Count; index++)
        {
            gaps.Add(checks[index].Day.DayNumber - checks[index - 1].Day.DayNumber);
        }

        return Signals.DailySeriesStatistics.Median(gaps);
    }

    private DateOnly Today()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(this.cadenceOptions.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(this.clock.GetUtcNow(), zone).Date);
    }

    /// <summary>Whether this service already spoke since the last check — once per lapse, never twice.</summary>
    private async Task<bool> SaidSinceAsync(DateOnly lastCheck, CancellationToken cancellationToken)
    {
        var since = new DateTimeOffset(lastCheck.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await foreach (var surfacing in this.surfacings.RecentAsync(this.cadenceOptions.RecentSurfacings, cancellationToken)
            .ConfigureAwait(false))
        {
            if (surfacing.ServiceName == this.ServiceName && surfacing.CreatedAt >= since)
            {
                return true;
            }
        }

        return false;
    }

    private Surfacing Surface(Check last, int count, double interval, int elapsed)
    {
        var body =
            $"Your last recorded INR was {last.Value.ToString("0.0", CultureInfo.InvariantCulture)} on {last.Day:yyyy-MM-dd}. "
            + $"Over {count} recorded checks you have kept about {interval:0} days between them; it has been {elapsed}. "
            + "Only a heads-up from the log — if there was a check I do not have, tell me and I will note it.";
        return new Surfacing(
            Guid.NewGuid(), this.ServiceName, "INR check: the interval you have kept has lapsed", body, 0.6, this.clock.GetUtcNow());
    }

    [GeneratedRegex(@"\bINR\b\D{0,24}?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValuedInr();

    private sealed record Check(DateOnly Day, double Value);
}
