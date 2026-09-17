using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Signals;

/// <summary>
/// The weekly correlation card: of every pair of daily series on this host, at lag zero
/// and lag one, the strongest correlation that clears the floor — with r, n, and the
/// admission that it is a correlation.
/// </summary>
/// <remarks>
/// Exist.io's flagship ("parcels delivered make me 136% more productive"): the praised
/// part is the surprise, and what keeps it honest is the evidence beside it. One card a
/// week at most (D-021); a pair with a sparse side is skipped rather than reported, and
/// the ledger gets nothing — a correlation is a fact about numbers, not a belief.
/// </remarks>
public sealed class CorrelationCardService : IProactiveService
{
    private readonly IReadOnlyList<IDailySeriesSource> sources;
    private readonly SignalsOptions signalsOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<CorrelationCardService> logger;

    /// <summary>Creates the service.</summary>
    public CorrelationCardService(
        IEnumerable<IDailySeriesSource> sources,
        IOptions<SignalsOptions> signalsOptions,
        TimeProvider clock,
        ILogger<CorrelationCardService> logger)
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
    public string ServiceName => "correlation-card";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var zone = this.signalsOptions.Zone;
        var to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(this.clock.GetUtcNow(), zone).Date).AddDays(-1);
        var from = to.AddDays(1 - this.signalsOptions.CorrelationWindowDays);

        var series = await this.CollectAsync(from, to, zone, cancellationToken).ConfigureAwait(false);
        var best = this.Strongest(series, from, to);
        if (best is null)
        {
            this.logger.LogInformation("Correlation card: {Count} series, nothing clears |r| >= {Floor}", series.Count, this.signalsOptions.MinimumCorrelation);
            return ProactiveResult.quiet;
        }

        return new ProactiveResult(
            Array.Empty<Conclusion>(), [this.Surface(best)], ProactiveStatus.Completed,
            $"{best.First.Metric} vs {best.Second.Metric} r={best.R:F2}");
    }

    /// <summary>Every series with enough data, tagged with the source it came from.</summary>
    private async Task<List<Sourced>> CollectAsync(
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var series = new List<Sourced>();
        for (var origin = 0; origin < this.sources.Count; origin++)
        {
            foreach (var item in await this.sources[origin].ReadAsync(from, to, zone, cancellationToken).ConfigureAwait(false))
            {
                if (ActiveDays(item) >= this.signalsOptions.CorrelationMinimumActiveDays)
                {
                    series.Add(new Sourced(origin, item));
                }
            }
        }

        return series;
    }

    /// <summary>
    /// The strongest pair across sources. Two series from one source are never paired:
    /// gym volume against working sets is arithmetic, not a finding.
    /// </summary>
    private Pair? Strongest(List<Sourced> series, DateOnly from, DateOnly to)
    {
        Pair? best = null;
        for (var first = 0; first < series.Count; first++)
        {
            for (var second = first + 1; second < series.Count; second++)
            {
                if (series[first].Origin == series[second].Origin)
                {
                    continue;
                }

                best = Stronger(best, this.Measure(series[first].Series, series[second].Series, from, to, 0));
                best = Stronger(best, this.Measure(series[first].Series, series[second].Series, from, to, 1));
            }
        }

        return best is not null && Math.Abs(best.R) >= this.signalsOptions.MinimumCorrelation ? best : null;
    }

    private Pair? Measure(DailySeries first, DailySeries second, DateOnly from, DateOnly to, int lag)
    {
        var (x, y) = DailySeriesStatistics.Align(first, second, from, to, lag);
        if (x.Length < this.signalsOptions.CorrelationMinimumDays)
        {
            return null;
        }

        var r = DailySeriesStatistics.Pearson(x, y);
        return r is null ? null : new Pair(first, second, lag, r.Value, x.Length);
    }

    private static Pair? Stronger(Pair? current, Pair? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        return current is null || Math.Abs(candidate.R) > Math.Abs(current.R) ? candidate : current;
    }

    private static int ActiveDays(DailySeries series)
    {
        var count = 0;
        foreach (var point in series.Points)
        {
            if (point.Value != 0)
            {
                count++;
            }
        }

        return count;
    }

    private Surfacing Surface(Pair pair)
    {
        var direction = pair.R > 0 ? "higher" : "lower";
        var when = pair.Lag == 0 ? string.Empty : " the next day";
        var body =
            $"More {pair.First.Label} goes with {direction} {pair.Second.Label}{when} "
            + $"(r = {pair.R:F2} over {pair.Days} days). Correlation, not cause; it could be nothing.";
        var confidence = Math.Min(0.8, Math.Abs(pair.R));
        return new Surfacing(
            Guid.NewGuid(), this.ServiceName, "Two things that move together", body, confidence, this.clock.GetUtcNow());
    }

    private sealed record Pair(DailySeries First, DailySeries Second, int Lag, double R, int Days);

    private sealed record Sourced(int Origin, DailySeries Series);
}
