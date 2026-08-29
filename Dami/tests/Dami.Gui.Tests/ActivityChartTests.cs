using Xunit;

namespace Dami.Gui.Tests;

public sealed class ActivityChartTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<int>> Counts(
        params (string Name, int[] Values)[] series)
    {
        return series.ToDictionary(
            item => item.Name,
            item => (IReadOnlyList<int>)item.Values);
    }

    [Fact]
    public void Build_Should_Share_One_Scale_Across_Series()
    {
        // Per-series scaling makes one tool call look as dramatic as forty trace events —
        // a dashboard lying while every number on it is true.
        var chart = ActivityChart.Build(Counts(
            ("turns", [0, 40]),
            ("tools", [0, 1])));

        var turns = chart.Single(item => item.Name == "turns");
        var tools = chart.Single(item => item.Name == "tools");

        Assert.Equal(0, turns.Line[1].Y);
        Assert.True(tools.Line[1].Y > ActivityChart.HEIGHT * 0.9, "one call must look small");
    }

    [Fact]
    public void Build_Should_Put_Zero_On_The_Baseline()
    {
        var chart = ActivityChart.Build(Counts(("turns", [0, 0, 0])));

        Assert.All(chart.Single().Line, point => Assert.Equal(ActivityChart.HEIGHT, point.Y));
    }

    [Fact]
    public void Build_Should_Span_The_Full_Width()
    {
        var chart = ActivityChart.Build(Counts(("turns", [1, 2, 3, 4])));

        var line = chart.Single().Line;
        Assert.Equal(0, line[0].X);
        Assert.Equal(ActivityChart.WIDTH, line[^1].X);
    }

    [Fact]
    public void Build_Should_Close_The_Area_Along_The_Baseline()
    {
        // Without the two closing points the fill renders as a stroked line, and the
        // chart reads as a sparkline rather than a load graph.
        var chart = ActivityChart.Build(Counts(("turns", [3, 1])));

        var area = chart.Single().Area;
        Assert.Equal(4, area.Count);
        Assert.Equal(ActivityChart.HEIGHT, area[^1].Y);
        Assert.Equal(ActivityChart.HEIGHT, area[^2].Y);
    }

    [Fact]
    public void Build_Should_Report_The_Latest_And_The_Peak()
    {
        var chart = ActivityChart.Build(Counts(("turns", [2, 9, 4])));

        var turns = chart.Single();
        Assert.Equal(4, turns.Now);
        Assert.Equal(9, turns.Peak);
    }

    [Fact]
    public void Build_Should_Survive_An_Idle_Runtime()
    {
        // Every series flat at zero must not divide by a zero ceiling.
        var chart = ActivityChart.Build(Counts(("turns", [0, 0]), ("tools", [0, 0])));

        Assert.Equal(2, chart.Count);
        Assert.All(chart, series => Assert.Equal(0, series.Peak));
    }

    [Fact]
    public void Build_Should_Ignore_A_Series_It_Has_No_Colour_For()
    {
        var chart = ActivityChart.Build(Counts(("turns", [1]), ("nonsense", [9])));

        Assert.Equal("turns", chart.Single().Name);
    }
}
