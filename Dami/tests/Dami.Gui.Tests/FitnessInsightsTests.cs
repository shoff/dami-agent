using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class FitnessInsightsTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static FitnessSnapshot Snapshot(
        IReadOnlyList<FitnessCardioSession>? cardio = null,
        IReadOnlyList<FitnessSet>? sets = null,
        IReadOnlyList<FitnessWeighIn>? weighIns = null)
    {
        return new FitnessSnapshot(cardio ?? [], sets ?? [], weighIns ?? []);
    }

    private static FitnessCardioSession Cardio(DateTimeOffset at)
    {
        return new FitnessCardioSession(
            Guid.NewGuid(), at, "treadmill", 1800, null, null, null, null, false, null);
    }

    private static FitnessSet Set(
        DateTimeOffset at, string exercise, short? reps, decimal? weight, string? muscle = null)
    {
        return new FitnessSet(
            Guid.NewGuid(), Guid.NewGuid(), at, exercise, muscle, 1, reps, weight, null, false);
    }

    [Fact]
    public void Build_Should_Flag_A_Cardio_Gap_Much_Longer_Than_The_Habit()
    {
        // Daily cardio for a week, then nine days of nothing: the gap is the news.
        var sessions = Enumerable.Range(9, 7).Select(daysAgo => Cardio(now.AddDays(-daysAgo)));

        var insights = FitnessInsights.Build(Snapshot(cardio: sessions.ToList()), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "gap" && insight.Text.Contains("cardio", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Stay_Quiet_When_The_Gap_Is_The_Usual_One()
    {
        // Proactive output is scarce by design (D-021); a dashboard that nags on an
        // ordinary rest day teaches its reader to ignore it.
        var sessions = new[] { Cardio(now.AddDays(-1)), Cardio(now.AddDays(-3)), Cardio(now.AddDays(-5)), Cardio(now.AddDays(-7)), Cardio(now.AddDays(-9)) };

        var insights = FitnessInsights.Build(Snapshot(cardio: sessions), now);

        Assert.DoesNotContain(insights, insight => insight.Kind == "gap");
    }

    [Fact]
    public void Build_Should_Report_A_Falling_Weight_Trend()
    {
        var weighIns = Enumerable.Range(0, 6)
            .Select(index => new FitnessWeighIn(now.AddDays(-25 + index * 5), 192m - index))
            .ToList();

        var insights = FitnessInsights.Build(Snapshot(weighIns: weighIns), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "weight" && insight.Text.Contains("down", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Call_A_Stable_Weight_Steady()
    {
        var weighIns = Enumerable.Range(0, 6)
            .Select(index => new FitnessWeighIn(now.AddDays(-25 + index * 5), 190m))
            .ToList();

        var insights = FitnessInsights.Build(Snapshot(weighIns: weighIns), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "weight" && insight.Text.Contains("steady", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Flag_A_Trained_Muscle_Group_Gone_Missing()
    {
        var history = Enumerable.Range(5, 12)
            .Select(weeksAgo => Set(now.AddDays(-7 * weeksAgo), "Lat Pulldown", 10, 120m, "back"));
        var recent = Enumerable.Range(0, 4)
            .Select(weeksAgo => Set(now.AddDays(-7 * weeksAgo - 1), "Bench Press", 10, 135m, "chest"));

        var insights = FitnessInsights.Build(Snapshot(sets: history.Concat(recent).ToList()), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "balance" && insight.Text.Contains("back", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Call_Out_A_Lift_That_Stopped_Moving()
    {
        var sets = Enumerable.Range(0, 6)
            .Select(index => Set(now.AddDays(-3 * (5 - index)).AddDays(-2), "Bench Press", 5, 185m))
            .ToList();

        var insights = FitnessInsights.Build(Snapshot(sets: sets), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "lift" && insight.Text.Contains("flat", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Celebrate_A_Lift_That_Is_Climbing()
    {
        var sets = Enumerable.Range(0, 6)
            .Select(index => Set(
                now.AddDays(-3 * (5 - index)).AddDays(-2), "Squat", 5, 200m + index * 10))
            .ToList();

        var insights = FitnessInsights.Build(Snapshot(sets: sets), now);

        Assert.Contains(insights, insight =>
            insight.Kind == "lift" && insight.Text.Contains("up", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Should_Report_A_Multi_Week_Streak()
    {
        var sessions = Enumerable.Range(1, 4)
            .SelectMany(week => new[]
            {
                Cardio(now.AddDays(-7 * week + 1)),
                Cardio(now.AddDays(-7 * week + 3)),
            })
            .ToList();

        var insights = FitnessInsights.Build(Snapshot(cardio: sessions), now);

        Assert.Contains(insights, insight => insight.Kind == "streak");
    }

    [Fact]
    public void Build_Should_Say_Nothing_About_An_Empty_Domain()
    {
        Assert.Empty(FitnessInsights.Build(Snapshot(), now));
    }
}
