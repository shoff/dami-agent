using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Core.Tests.Fitness;

/// <summary>What the frontier is handed to compare a fresh set against.</summary>
public sealed class FitnessHistoryTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 6, 20, 0, 0, TimeSpan.Zero);

    private static FitnessSet Set(string exercise, int daysAgo, decimal weight, short reps, short? rpe = null, Guid? session = null, short number = 1, bool warmup = false) =>
        new(Guid.NewGuid(), session ?? Guid.NewGuid(), now.AddDays(-daysAgo), exercise, null, number, reps, weight, rpe, warmup);

    [Fact]
    public void Known_Exercises_Should_Be_Most_Recently_Trained_First_In_The_Logs_Spelling()
    {
        var log = new FitnessSnapshot([], [Set("Leg Press", 30, 300, 10), Set("row", 2, 100, 10), Set("leg press", 1, 310, 10)], []);

        Assert.Equal(["leg press", "row"], FitnessHistory.KnownExercises(log));
    }

    [Fact]
    public void Resolve_Should_Prefer_The_Logs_Spelling_And_Keep_A_New_Name()
    {
        string[] known = ["biceps curl machine", "preacher curl machine"];

        Assert.Equal("biceps curl machine", FitnessHistory.Resolve(known, "Biceps  Curl Machine "));
        Assert.Equal("biceps curl (Hammer Strength)", FitnessHistory.Resolve(known, "biceps curl (Hammer Strength)"));
    }

    [Fact]
    public void Describe_Should_Give_The_Last_Three_Sessions_And_The_Best_Excluding_Today()
    {
        var today = Guid.NewGuid();
        var lastWeek = Guid.NewGuid();
        var log = new FitnessSnapshot(
            [],
            [
                Set("leg press", 40, 280, 10, 6), Set("leg press", 30, 290, 10, 7), Set("leg press", 20, 300, 10, 7),
                Set("leg press", 7, 300, 12, 8, lastWeek), Set("leg press", 7, 300, 10, 8, lastWeek, 2),
                Set("leg press", 7, 200, 10, 3, lastWeek, 0, warmup: true),
                Set("leg press", 0, 320, 10, 7, today), Set("row", 1, 100, 10),
            ],
            []);

        var text = FitnessHistory.Describe(log, "leg press", today);

        Assert.StartsWith("Before today on leg press (4 session(s)): 2026-08-30: 2x10-12 at 300 lb RPE 8; 2026-08-17: 1x10 at 300 lb RPE 7; 2026-08-07: 1x10 at 290 lb RPE 7.", text, StringComparison.Ordinal);
        Assert.EndsWith("Best before today: 300 lb × 12 on 2026-08-30.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Say_So_When_The_Exercise_Is_New()
    {
        var log = new FitnessSnapshot([], [Set("row", 1, 100, 10)], []);

        Assert.Equal("First time on leg press in the log.", FitnessHistory.Describe(log, "leg press", Guid.NewGuid()));
    }
}
