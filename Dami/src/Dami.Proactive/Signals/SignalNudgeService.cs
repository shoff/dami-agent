using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Signals;

/// <summary>
/// The nightly single-fact nudge: "yesterday's gym volume was the highest in six weeks"
/// — one fact, its window, its count, and nothing else.
/// </summary>
/// <remarks>
/// Exist.io users named exactly this as the nudge that worked, and the disliked list
/// (nightly grades, guilt streaks) is what it must not become: a day with no data is a
/// rest day, not a low; an ordinary day is silence; at most one fact a night, chosen by
/// how far it sits from the window's median. Everything read here is local (D-012).
/// </remarks>
public sealed class SignalNudgeService : IProactiveService
{
    private readonly IReadOnlyList<IDailySeriesSource> sources;
    private readonly SignalsOptions signalsOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<SignalNudgeService> logger;

    /// <summary>Creates the service.</summary>
    public SignalNudgeService(
        IEnumerable<IDailySeriesSource> sources,
        IOptions<SignalsOptions> signalsOptions,
        TimeProvider clock,
        ILogger<SignalNudgeService> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(signalsOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.sources = [.. sources];
        this.signalsOptions = signalsOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "signal-nudge";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var zone = this.signalsOptions.Zone;
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(this.clock.GetUtcNow(), zone).Date).AddDays(-1);
        var from = yesterday.AddDays(1 - this.signalsOptions.NudgeWindowDays);

        Extreme? best = null;
        var examined = 0;
        foreach (var source in this.sources)
        {
            foreach (var series in await source.ReadAsync(from, yesterday, zone, cancellationToken).ConfigureAwait(false))
            {
                examined++;
                var extreme = this.Examine(series, yesterday);
                if (extreme is not null && (best is null || extreme.Deviation > best.Deviation))
                {
                    best = extreme;
                }
            }
        }

        if (best is null)
        {
            this.logger.LogInformation("Signal nudge: {Count} series examined for {Day}; nothing was a high or low", examined, yesterday);
            return ProactiveResult.quiet;
        }

        this.logger.LogInformation("Signal nudge: {Metric} was the {Kind} of {Count} day(s)", best.Series.Metric, best.Kind, best.Count);
        return new ProactiveResult(
            Array.Empty<Conclusion>(), [this.Surface(best, yesterday)], ProactiveStatus.Completed, best.Series.Metric);
    }

    /// <summary>Yesterday's point against the window's other days with data, or null.</summary>
    private Extreme? Examine(DailySeries series, DateOnly yesterday)
    {
        var others = new List<double>();
        double? own = null;
        foreach (var point in series.Points)
        {
            if (point.Value == 0)
            {
                continue;
            }

            if (point.Day == yesterday)
            {
                own = point.Value;
            }
            else
            {
                others.Add(point.Value);
            }
        }

        if (own is null || others.Count + 1 < this.signalsOptions.NudgeMinimumPoints)
        {
            return null;
        }

        return Classify(series, own.Value, others);
    }

    private static Extreme? Classify(DailySeries series, double own, List<double> others)
    {
        var max = double.MinValue;
        var min = double.MaxValue;
        foreach (var value in others)
        {
            max = Math.Max(max, value);
            min = Math.Min(min, value);
        }

        var median = DailySeriesStatistics.Median(others);
        var deviation = median == 0 ? Math.Abs(own) : Math.Abs(own - median) / Math.Abs(median);
        if (own > max)
        {
            return new Extreme(series, "highest", own, median, others.Count + 1, deviation);
        }

        return own < min ? new Extreme(series, "lowest", own, median, others.Count + 1, deviation) : null;
    }

    private Surfacing Surface(Extreme extreme, DateOnly yesterday)
    {
        var weeks = Math.Max(1, this.signalsOptions.NudgeWindowDays / 7);
        var title = $"{Capitalize(extreme.Series.Label)}: {extreme.Kind} in {weeks} weeks";
        var body =
            $"Yesterday ({yesterday:yyyy-MM-dd}) your {extreme.Series.Label} was {extreme.Value:N0} {extreme.Series.Unit} — "
            + $"the {extreme.Kind} in the last {weeks} weeks ({extreme.Count} days with data; median {extreme.Median:N0} {extreme.Series.Unit}). "
            + "A trend, not a grade.";
        return new Surfacing(Guid.NewGuid(), this.ServiceName, title, body, 0.6, this.clock.GetUtcNow());
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private sealed record Extreme(DailySeries Series, string Kind, double Value, double Median, int Count, double Deviation);
}
