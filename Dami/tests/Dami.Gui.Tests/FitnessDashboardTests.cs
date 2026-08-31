using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class FitnessDashboardTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static FitnessSet Set(DateTimeOffset at, string exercise, short? reps, decimal? weight)
    {
        return new FitnessSet(
            Guid.NewGuid(), Guid.NewGuid(), at, exercise, null, 1, reps, weight, null, false);
    }

    private static FitnessCardioSession Cardio(DateTimeOffset at, int seconds)
    {
        return new FitnessCardioSession(
            Guid.NewGuid(), at, "treadmill", seconds, 2.0m, null, 140, null, false, null);
    }

    [Fact]
    public void Tiles_Should_Report_The_Latest_Weigh_In()
    {
        var snapshot = new FitnessSnapshot(
            [], [],
            [new FitnessWeighIn(now.AddDays(-40), 194.0m), new FitnessWeighIn(now.AddDays(-2), 189.2m)]);

        var tiles = FitnessDashboard.Tiles(snapshot, now);

        var weight = Assert.Single(tiles, tile => tile.Label == "BODY WEIGHT");
        Assert.Contains("189.2", weight.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Tiles_Should_Count_This_Weeks_Sessions_Against_Last_Weeks()
    {
        var snapshot = new FitnessSnapshot(
            [Cardio(now.AddDays(-1), 1800), Cardio(now.AddDays(-9), 1800)],
            [Set(now.AddDays(-2), "Squat", 5, 225m)],
            []);

        var tiles = FitnessDashboard.Tiles(snapshot, now);

        var sessions = Assert.Single(tiles, tile => tile.Label == "SESSIONS · 7 DAYS");
        Assert.Equal(("2", "1 the week before"), (sessions.Value, sessions.Detail));
    }

    [Fact]
    public void Exercises_Should_List_Most_Trained_First()
    {
        var sets = new[]
        {
            Set(now.AddDays(-1), "Squat", 5, 225m),
            Set(now.AddDays(-2), "Bench Press", 8, 135m),
            Set(now.AddDays(-4), "Bench Press", 8, 140m),
        };

        var exercises = FitnessDashboard.Exercises(sets);

        Assert.Equal(["Bench Press", "Squat"], exercises.Select(choice => choice.Name).ToList());
    }

    [Fact]
    public void RecentSessions_Should_Merge_All_Kinds_Newest_First()
    {
        var snapshot = new FitnessSnapshot(
            [Cardio(now.AddDays(-3), 1800)],
            [Set(now.AddDays(-1), "Squat", 5, 225m), Set(now.AddDays(-1), "Squat", 5, 235m)],
            [new FitnessWeighIn(now.AddDays(-2), 189.2m)]);

        var rows = FitnessDashboard.RecentSessions(snapshot, limit: 10);

        Assert.Equal(3, rows.Count);
        Assert.Contains("Squat", rows[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentSessions_Should_Respect_The_Limit()
    {
        var cardio = Enumerable.Range(1, 20).Select(day => Cardio(now.AddDays(-day), 600)).ToList();

        var rows = FitnessDashboard.RecentSessions(new FitnessSnapshot(cardio, [], []), limit: 5);

        Assert.Equal(5, rows.Count);
    }
}
