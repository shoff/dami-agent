using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ExerciseTrendTests
{
    private static readonly DateTimeOffset day = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static FitnessSet Set(
        DateTimeOffset at, string exercise, short? reps, decimal? weight, bool warmup = false)
    {
        return new FitnessSet(
            Guid.NewGuid(), Guid.NewGuid(), at, exercise, null, 1, reps, weight, null, warmup);
    }

    [Fact]
    public void Estimate1Rm_Should_Apply_Epley_For_Multiple_Reps()
    {
        // 100 lb × 10 reps → 100 × (1 + 10/30) ≈ 133.3
        Assert.Equal(133.3, ExerciseTrend.Estimate1Rm(100, 10), precision: 1);
    }

    [Fact]
    public void Estimate1Rm_Should_Return_The_Weight_Itself_For_A_Single()
    {
        Assert.Equal(315, ExerciseTrend.Estimate1Rm(315, 1));
    }

    [Fact]
    public void Days_Should_Pick_The_Best_Set_Of_Each_Day()
    {
        var sets = new[]
        {
            Set(day, "Bench Press", 10, 100m),
            Set(day.AddHours(1), "Bench Press", 5, 185m),
            Set(day, "Squat", 5, 225m),
        };

        var days = ExerciseTrend.Days(sets, "Bench Press");

        Assert.Equal((1, 185d, 5), (days.Count, days[0].TopWeight, days[0].RepsAtTop));
    }

    [Fact]
    public void Days_Should_Ignore_Warmups_And_Unweighted_Sets()
    {
        var sets = new[]
        {
            Set(day, "Bench Press", 10, 225m, warmup: true),
            Set(day, "Bench Press", null, 135m),
            Set(day, "Bench Press", 8, 135m),
        };

        var days = ExerciseTrend.Days(sets, "Bench Press");

        Assert.Equal(135d, Assert.Single(days).TopWeight);
    }
}
