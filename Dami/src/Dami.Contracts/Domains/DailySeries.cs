namespace Dami.Contracts.Domains;

/// <summary>
/// A named daily series — one number per day that has data — the raw material for the
/// single-fact nudge and the weekly correlation card. Days without data are absent, not
/// zero; the reader decides what absence means for its metric.
/// </summary>
public sealed record DailySeries
{
    /// <summary>Creates a series.</summary>
    /// <param name="metric">A stable identifier, e.g. <c>gym-volume</c>.</param>
    /// <param name="label">How the metric is named to Steve, e.g. "gym volume".</param>
    /// <param name="unit">The unit of the value, e.g. "lb".</param>
    /// <param name="points">The days with data, ascending by day.</param>
    public DailySeries(string metric, string label, string unit, IReadOnlyList<DailyPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(points);
        this.Metric = metric;
        this.Label = label;
        this.Unit = unit;
        this.Points = points;
    }

    /// <summary>The stable identifier.</summary>
    public string Metric { get; }

    /// <summary>The human name.</summary>
    public string Label { get; }

    /// <summary>The unit of <see cref="DailyPoint.Value"/>.</summary>
    public string Unit { get; }

    /// <summary>The days with data, ascending.</summary>
    public IReadOnlyList<DailyPoint> Points { get; }
}
