using Dami.Contracts.Domains;
using Dami.Proactive.Signals;
using Xunit;

namespace Dami.Proactive.Tests.Signals;

public sealed class DailySeriesStatisticsTests
{
    private static readonly DateOnly start = new(2026, 9, 1);

    [Fact]
    public void Median_Should_Take_The_Middle_Of_An_Odd_Count()
    {
        Assert.Equal(5.0, DailySeriesStatistics.Median([9.0, 1.0, 5.0]));
    }

    [Fact]
    public void Median_Should_Average_The_Middle_Pair_Of_An_Even_Count()
    {
        Assert.Equal(3.0, DailySeriesStatistics.Median([1.0, 2.0, 4.0, 5.0]));
    }

    [Fact]
    public void Pearson_Should_Be_One_For_A_Perfect_Line()
    {
        var r = DailySeriesStatistics.Pearson([1.0, 2.0, 3.0, 4.0], [2.0, 4.0, 6.0, 8.0]);

        Assert.Equal(1.0, r!.Value, 9);
    }

    [Fact]
    public void Pearson_Should_Be_Null_When_A_Series_Never_Moves()
    {
        Assert.Null(DailySeriesStatistics.Pearson([1.0, 2.0, 3.0], [4.0, 4.0, 4.0]));
    }

    [Fact]
    public void Align_Should_Fill_Missing_Days_With_Zero()
    {
        var a = Series("a", (0, 1.0), (2, 3.0));
        var b = Series("b", (1, 5.0));

        var (x, y) = DailySeriesStatistics.Align(a, b, start, start.AddDays(2), 0);

        Assert.Equal(new[] { 1.0, 0.0, 3.0 }, x);
        Assert.Equal(new[] { 0.0, 5.0, 0.0 }, y);
    }

    [Fact]
    public void Align_Should_Shift_The_Second_Series_By_The_Lag()
    {
        var a = Series("a", (0, 1.0), (1, 2.0), (2, 3.0));
        var b = Series("b", (1, 10.0), (2, 20.0));

        var (_, y) = DailySeriesStatistics.Align(a, b, start, start.AddDays(2), 1);

        Assert.Equal(new[] { 10.0, 20.0 }, y);
    }

    private static DailySeries Series(string metric, params (int Offset, double Value)[] points)
    {
        var list = new List<DailyPoint>();
        foreach (var (offset, value) in points)
        {
            list.Add(new DailyPoint(start.AddDays(offset), value));
        }

        return new DailySeries(metric, metric, "units", list);
    }
}
