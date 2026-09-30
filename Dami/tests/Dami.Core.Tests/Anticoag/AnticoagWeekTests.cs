using Dami.Contracts.Anticoag;
using Dami.Contracts.Nutrition;
using Dami.Core.Anticoag;
using Xunit;

namespace Dami.Core.Tests.Anticoag;

/// <summary>The Monday note (ADR-0037 slice 2): only what the data supports, never advice on dose.</summary>
public sealed class AnticoagWeekTests
{
    // Monday 2026-10-26, 14:00 UTC.
    private static readonly DateTimeOffset now = new(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

    private static InrReading Reading(int daysAgo, decimal inr) => new(Guid.NewGuid(), today.AddDays(-daysAgo), inr, now);

    private static Meal Meal(int daysAgo, string? vitaminK) =>
        new(Guid.NewGuid(), now.AddDays(-daysAgo), "m", 500, 20, now) { VitaminK = vitaminK };

    [Fact]
    public void A_Swing_In_High_Vitamin_K_Meals_Against_A_Known_Usual_Should_Be_Said_With_The_Principle()
    {
        // Usual: one high-K meal a week for four weeks; this week: four.
        List<Meal> meals = [Meal(1, "high"), Meal(2, "high"), Meal(3, "high"), Meal(4, "high"), Meal(5, "low"),
            Meal(10, "high"), Meal(17, "high"), Meal(24, "high"), Meal(31, "high"), Meal(34, "low")];

        var lines = AnticoagWeek.Lines([], [], meals, now);

        var line = Assert.Single(lines);
        Assert.Contains("4 high-vitamin-K meals this week, against about 1 a week before", line, StringComparison.Ordinal);
        Assert.Contains("steady intake", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_Two_Weeks_Of_History_There_Should_Be_No_Usual()
    {
        var lines = AnticoagWeek.Lines([], [], [Meal(1, "high"), Meal(2, "high")], now);

        Assert.Equal(["2 high-vitamin-K meals this week; not enough history yet to know your usual."], lines);
    }

    [Fact]
    public void A_Check_Past_His_Own_Rhythm_Should_Be_Said_And_Without_Three_Readings_It_Should_Not()
    {
        var overdue = AnticoagWeek.Lines([Reading(12, 2.4m), Reading(19, 2.2m), Reading(26, 2.6m)], [], [], now);
        var tooFew = AnticoagWeek.Lines([Reading(12, 2.4m), Reading(19, 2.2m)], [], [], now);

        Assert.Contains(overdue, line => line.Contains("Last INR 2.4 on Oct 14, 12 days ago", StringComparison.Ordinal)
            && line.Contains("longer than your usual 7 days between checks", StringComparison.Ordinal));
        Assert.DoesNotContain(tooFew, line => line.Contains("usual", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Dose_On_File_Should_Be_Quoted_Not_Discussed()
    {
        var lines = AnticoagWeek.Lines([], [new DoseChange(Guid.NewGuid(), today.AddDays(-3), "7.5 mg Mon/Wed, 5 mg other days", now)], [], now);

        Assert.Equal(["Dose on file since Oct 23: “7.5 mg Mon/Wed, 5 mg other days”."], lines);
    }

    [Fact]
    public void Nothing_Logged_Should_Mean_Nothing_To_Say()
    {
        Assert.Empty(AnticoagWeek.Lines([], [], [Meal(1, null)], now));
    }
}
