using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class FitnessChartsTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static FitnessSet Set(
        DateTimeOffset at, string exercise, short? reps, decimal? weight, bool warmup = false)
    {
        return new FitnessSet(
            Guid.NewGuid(), Guid.NewGuid(), at, exercise, null, 1, reps, weight, null, warmup);
    }

    [Fact]
    public void WeeklyTonnage_Should_Sum_Weight_Times_Reps_Into_The_Right_Week()
    {
        var sets = new[]
        {
            Set(now.AddDays(-1), "Bench Press", 10, 100m),
            Set(now.AddDays(-8), "Bench Press", 5, 200m),
        };

        var totals = FitnessCharts.WeeklyTonnage(sets, now, weeks: 4);

        Assert.Equal([0d, 0d, 1000d, 1000d], totals);
    }

    [Fact]
    public void WeeklyTonnage_Should_Exclude_Warmups()
    {
        // A warmup set is preparation, not volume; counting it would reward longer
        // ramp-ups over heavier work.
        var sets = new[]
        {
            Set(now.AddDays(-1), "Squat", 10, 45m, warmup: true),
            Set(now.AddDays(-1), "Squat", 5, 225m),
        };

        var totals = FitnessCharts.WeeklyTonnage(sets, now, weeks: 1);

        Assert.Equal([1125d], totals);
    }

    [Fact]
    public void WeeklyCardioMinutes_Should_Bucket_Duration_By_Week()
    {
        var cardio = new[]
        {
            new FitnessCardioSession(
                Guid.NewGuid(), now.AddDays(-2), "treadmill", 1800, null, null, null, null, false, null),
            new FitnessCardioSession(
                Guid.NewGuid(), now.AddDays(-9), "rowing", 600, null, null, null, null, false, null),
        };

        var totals = FitnessCharts.WeeklyCardioMinutes(cardio, now, weeks: 2);

        Assert.Equal([10d, 30d], totals);
    }

    [Fact]
    public void Trend_Should_Scale_Between_Padded_Floor_And_Ceiling()
    {
        // Body weight lives in a narrow band; scaled from zero the line would be flat
        // and the chart would say nothing while every number on it was true.
        var series = FitnessCharts.Trend(
            "body weight",
            [(now.AddDays(-10), 180d), (now, 190d)],
            "#5AA9E6",
            "lb");

        Assert.NotNull(series);
        Assert.True(series.Line[0].Y > series.Line[1].Y);
    }

    [Fact]
    public void Trend_Should_Return_Null_For_No_Points()
    {
        Assert.Null(FitnessCharts.Trend("empty", [], "#5AA9E6", "lb"));
    }

    [Fact]
    public void Trend_Should_Place_Points_By_Time_Not_By_Index()
    {
        // Weigh-ins are irregular. Index spacing would draw a two-month gap and a
        // two-day gap the same width, which misstates every slope on the chart.
        var series = FitnessCharts.Trend(
            "body weight",
            [(now.AddDays(-100), 190d), (now.AddDays(-90), 189d), (now, 185d)],
            "#5AA9E6",
            "lb");

        Assert.True(
            series!.Line[1].X - series.Line[0].X < (series.Line[2].X - series.Line[1].X) / 2);
    }

    [Fact]
    public void Weekly_Should_Report_The_Latest_Bucket_As_Now()
    {
        var series = FitnessCharts.Weekly("tonnage", [500d, 1200d], "#4CB782", "lb");

        Assert.Equal("1,200 lb", series!.Now);
    }
}
