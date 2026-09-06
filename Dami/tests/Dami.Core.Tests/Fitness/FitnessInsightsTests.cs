using Dami.Contracts.Domains;
using Xunit;

namespace Dami.Core.Tests.Fitness;

/// <summary>Every sentence carries its numbers, so each rule is checked against a hand-built log.</summary>
public sealed class FitnessInsightsTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 5, 20, 0, 0, TimeSpan.Zero);

    private static FitnessSet Set(string exercise, int daysAgo, decimal weight, short reps, short? rpe = null, string? group = null, Guid? session = null) =>
        new(Guid.NewGuid(), session ?? Guid.NewGuid(), now.AddDays(-daysAgo), exercise, group, 1, reps, weight, rpe, false);

    private static FitnessSnapshot Log(params FitnessSet[] sets) => new([], sets, []);

    [Fact]
    public void A_Session_That_Beats_Every_Earlier_Best_Is_A_Personal_Record_With_The_Numbers()
    {
        var insights = FitnessInsights.Analyze(Log(
            Set("leg press", 30, 300, 10), Set("leg press", 15, 320, 10), Set("leg press", 0, 340, 10)), now);

        var pr = Assert.Single(insights, insight => insight.Kind == FitnessInsightKind.PersonalRecord);
        Assert.Contains("340 lb × 10", pr.Text, StringComparison.Ordinal);
        Assert.Contains("up from 320 lb × 10", pr.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Record_Is_Only_Announced_For_A_Fresh_Session()
    {
        var insights = FitnessInsights.Analyze(Log(Set("row", 40, 100, 10), Set("row", 10, 120, 10)), now);

        Assert.DoesNotContain(insights, insight => insight.Kind == FitnessInsightKind.PersonalRecord);
    }

    [Fact]
    public void Six_Flat_Weeks_Over_Four_Sessions_Is_A_Plateau()
    {
        var insights = FitnessInsights.Analyze(Log(
            Set("bench", 70, 185, 5), Set("bench", 56, 185, 5), Set("bench", 30, 185, 5), Set("bench", 10, 185, 5), Set("bench", 3, 185, 5)), now);

        var plateau = Assert.Single(insights, insight => insight.Kind == FitnessInsightKind.Plateau);
        Assert.Contains("bench has been flat", plateau.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Group_Trained_Lately_But_Not_For_Two_Weeks_Is_Neglected()
    {
        var insights = FitnessInsights.Analyze(Log(
            Set("squat", 40, 225, 5, group: "legs"), Set("squat", 25, 225, 5, group: "legs"), Set("curl", 1, 40, 12, group: "biceps")), now);

        var neglected = Assert.Single(insights, insight => insight.Kind == FitnessInsightKind.Neglected);
        Assert.StartsWith("No legs work since", neglected.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Easy_Sets_Suggest_Five_More_Pounds_And_Hard_Sets_Suggest_Holding()
    {
        var easy = Guid.NewGuid();
        var hard = Guid.NewGuid();
        var insights = FitnessInsights.Analyze(Log(
            Set("curl", 0, 140, 12, 7, session: easy), Set("curl", 0, 140, 12, 6, session: easy),
            Set("press", 0, 70, 8, 9, session: hard)), now);

        var suggestions = insights.Where(insight => insight.Kind == FitnessInsightKind.Suggestion).ToList();
        Assert.Contains(suggestions, s => s.Exercise == "curl" && s.Text.Contains("try 145 lb next time", StringComparison.Ordinal));
        Assert.Contains(suggestions, s => s.Exercise == "press" && s.Text.Contains("hold the weight", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Weekly_Summary_Compares_This_Week_With_Last()
    {
        var snapshot = new FitnessSnapshot(
            [new FitnessCardioSession(Guid.NewGuid(), now.AddDays(-2), "treadmill", 1800, null, null, null, null, false, null)],
            [Set("curl", 1, 100, 10), Set("curl", 9, 100, 10)],
            []);

        var week = Assert.Single(FitnessInsights.Analyze(snapshot, now), insight => insight.Kind == FitnessInsightKind.WeeklySummary);

        Assert.Contains("This week: 1 lifting session(s), 1 sets, 1,000 lb moved, 30 min cardio", week.Text, StringComparison.Ordinal);
        Assert.Contains("Last week: 1, 1 sets, 1,000 lb, 0 min", week.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ForExercise_Keeps_Only_That_Exercise()
    {
        var log = Log(Set("curl", 30, 100, 10), Set("curl", 0, 120, 10), Set("row", 0, 100, 10, 6));

        var forCurl = FitnessInsights.ForExercise(log, "Curl", now);

        Assert.All(forCurl, insight => Assert.Equal("curl", insight.Exercise));
        Assert.Contains(forCurl, insight => insight.Kind == FitnessInsightKind.PersonalRecord);
    }

    [Fact]
    public void An_Empty_Log_Says_Nothing()
    {
        Assert.Empty(FitnessInsights.Analyze(Log(), now));
    }
}
