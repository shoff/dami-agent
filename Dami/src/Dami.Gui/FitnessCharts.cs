using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>One drawable fitness series with its legend, in the fixed 1000×200 space.</summary>
public sealed record FitnessSeries(
    string Name,
    IBrush Stroke,
    IBrush Fill,
    Points Line,
    Points Area,
    string Now,
    string Floor,
    string Ceiling);

/// <summary>Turns fitness rows into something drawable. Pure, like <see cref="ActivityChart"/>.</summary>
/// <remarks>
/// Two scales on purpose. Weekly totals start at zero because a bar of work compared to
/// no work is the honest comparison; body weight is min–max scaled because it lives in a
/// narrow band, and scaled from zero the line is flat and says nothing while every number
/// on it is true.
/// </remarks>
public static class FitnessCharts
{
    /// <summary>Plot width in the fixed drawing space.</summary>
    public const double WIDTH = 1000;

    /// <summary>Plot height in the fixed drawing space.</summary>
    public const double HEIGHT = 200;

    /// <summary>A time-scaled trend line, min–max padded. Null when there is nothing to draw.</summary>
    public static FitnessSeries? Trend(
        string name,
        IReadOnlyList<(DateTimeOffset At, double Value)> points,
        string colour,
        string unit)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return null;
        }

        var floor = points.Min(point => point.Value);
        var ceiling = points.Max(point => point.Value);
        var pad = Math.Max((ceiling - floor) * 0.1, 1.0);
        floor -= pad;
        ceiling += pad;

        var first = points[0].At;
        var span = Math.Max((points[^1].At - first).TotalDays, 1.0);
        var line = new Points();
        foreach (var (at, value) in points)
        {
            line.Add(new Point(
                (at - first).TotalDays / span * WIDTH,
                HEIGHT - ((value - floor) / (ceiling - floor) * HEIGHT)));
        }

        return Assemble(name, colour, line, Label(points[^1].Value, unit),
            Label(floor, unit), Label(ceiling, unit));
    }

    /// <summary>Weekly totals, oldest first, scaled from zero. Null when empty.</summary>
    public static FitnessSeries? Weekly(
        string name, IReadOnlyList<double> totals, string colour, string unit)
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (totals.Count == 0)
        {
            return null;
        }

        var ceiling = Math.Max(totals.Max(), 1.0);
        var line = new Points();
        for (var index = 0; index < totals.Count; index++)
        {
            line.Add(new Point(
                totals.Count <= 1 ? WIDTH : index * WIDTH / (totals.Count - 1),
                HEIGHT - (totals[index] / ceiling * HEIGHT)));
        }

        return Assemble(name, colour, line, Label(totals[^1], unit), Label(0, unit), Label(ceiling, unit));
    }

    /// <summary>Working-set tonnage per trailing 7-day block, oldest block first.</summary>
    public static IReadOnlyList<double> WeeklyTonnage(
        IReadOnlyList<FitnessSet> sets, DateTimeOffset now, int weeks)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeks);

        var totals = new double[weeks];
        foreach (var set in sets)
        {
            if (set.IsWarmup || set.WeightLbs is not { } weight || set.Reps is not { } reps)
            {
                continue;
            }

            var block = (int)((now - set.OccurredAt).TotalDays / 7);
            if (block >= 0 && block < weeks)
            {
                totals[weeks - 1 - block] += (double)weight * reps;
            }
        }

        return totals;
    }

    /// <summary>Cardio minutes per trailing 7-day block, oldest block first.</summary>
    public static IReadOnlyList<double> WeeklyCardioMinutes(
        IReadOnlyList<FitnessCardioSession> cardio, DateTimeOffset now, int weeks)
    {
        ArgumentNullException.ThrowIfNull(cardio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeks);

        var totals = new double[weeks];
        foreach (var session in cardio)
        {
            var block = (int)((now - session.OccurredAt).TotalDays / 7);
            if (block >= 0 && block < weeks && session.DurationSeconds is { } seconds)
            {
                totals[weeks - 1 - block] += seconds / 60.0;
            }
        }

        return totals;
    }

    private static FitnessSeries Assemble(
        string name, string colour, Points line, string now, string floor, string ceiling)
    {
        // The line closed along the baseline gives the filled look; a stroked line alone
        // reads as a sparkline rather than a trend.
        var area = new Points(line);
        if (line.Count > 0)
        {
            area.Add(new Point(line[^1].X, HEIGHT));
            area.Add(new Point(line[0].X, HEIGHT));
        }

        return new FitnessSeries(
            name,
            new SolidColorBrush(Color.Parse(colour)),
            new SolidColorBrush(Color.Parse(colour), 0.18),
            line, area, now, floor, ceiling);
    }

    private static string Label(double value, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{value:N0} {unit}");
}
