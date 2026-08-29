using Avalonia;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>One plotted series: its shape, its colour, and where it stands now.</summary>
public sealed record ActivitySeries(
    string Name,
    IReadOnlyList<int> Values,
    IBrush Stroke,
    IBrush Fill,
    Points Line,
    Points Area,
    int Now,
    int Peak);

/// <summary>Turns bucketed counts into something drawable.</summary>
/// <remarks>
/// Plotted into a fixed 1000×200 space and scaled by a <c>Viewbox</c>, so the geometry
/// never has to know the panel's real size. That keeps this pure and testable: a chart
/// that needs a laid-out control to compute its own points cannot be checked without a
/// window, and every mistake in it then shows up only as a wrong-looking picture.
///
/// All series share one vertical scale, taken from the busiest of them. Per-series scaling
/// would make a single tool call look as dramatic as forty trace events, which is the
/// classic way a dashboard lies while every number on it is true.
/// </remarks>
public static class ActivityChart
{
    /// <summary>Plot width in the fixed drawing space.</summary>
    public const double WIDTH = 1000;

    /// <summary>Plot height in the fixed drawing space.</summary>
    public const double HEIGHT = 200;

    private static readonly (string Name, string Colour)[] palette =
    [
        ("turns", "#5AA9E6"),
        ("tools", "#B98CE0"),
        ("egress", "#D9A441"),
        ("workers", "#4CB782"),
        ("produced", "#E0604F"),
    ];

    /// <summary>Builds the drawable series, busiest last so it paints on top.</summary>
    public static IReadOnlyList<ActivitySeries> Build(
        IReadOnlyDictionary<string, IReadOnlyList<int>> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var ceiling = counts.Values.SelectMany(values => values).DefaultIfEmpty(0).Max();
        return palette
            .Where(entry => counts.ContainsKey(entry.Name))
            .Select(entry => Plot(entry.Name, counts[entry.Name], entry.Colour, ceiling))
            .ToList();
    }

    private static ActivitySeries Plot(string name, IReadOnlyList<int> values, string colour, int ceiling)
    {
        var stroke = new SolidColorBrush(Color.Parse(colour));
        var fill = new SolidColorBrush(Color.Parse(colour), 0.18);
        var line = new Points();
        for (var index = 0; index < values.Count; index++)
        {
            line.Add(new Point(X(index, values.Count), Y(values[index], ceiling)));
        }

        // The area is the line closed along the baseline, which is what gives the filled
        // look; a stroked line alone reads as a sparkline rather than a load graph.
        var area = new Points(line);
        if (line.Count > 0)
        {
            area.Add(new Point(line[^1].X, HEIGHT));
            area.Add(new Point(line[0].X, HEIGHT));
        }

        return new ActivitySeries(
            name, values, stroke, fill, line, area,
            values.Count == 0 ? 0 : values[^1],
            values.DefaultIfEmpty(0).Max());
    }

    private static double X(int index, int count) =>
        count <= 1 ? 0 : index * WIDTH / (count - 1);

    /// <remarks>
    /// A count of zero sits exactly on the baseline rather than a pixel above it, so an
    /// idle series reads as flat rather than as a very small amount of something.
    /// </remarks>
    private static double Y(int value, int ceiling) =>
        ceiling <= 0 ? HEIGHT : HEIGHT - (value * HEIGHT / ceiling);
}
