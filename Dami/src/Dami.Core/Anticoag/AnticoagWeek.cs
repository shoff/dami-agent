using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Anticoag;
using Dami.Contracts.Calendar;
using Dami.Contracts.Nutrition;

namespace Dami.Core.Anticoag;

/// <summary>The lines of the Monday anticoagulation note (ADR-0037 slice 2): only what the data supports.</summary>
/// <remarks>
/// No interval claim from fewer than three readings, no "usual" from less than two weeks of
/// meal history, no vitamin-K line unless the week differs from the usual by two meals or
/// more — and never a word about what the dose should be.
/// </remarks>
public static partial class AnticoagWeek
{
    private const int MINIMUM_READINGS = 3;
    private const int USUAL_WEEKS = 4;
    private const double OVERDUE_FACTOR = 1.5;

    /// <summary>What there is to say this week; empty when nothing is logged.</summary>
    public static IReadOnlyList<string> Lines(
        IReadOnlyList<InrReading> readings, IReadOnlyList<DoseChange> doses, IReadOnlyList<Meal> meals,
        IReadOnlyList<CalendarEvent> upcoming, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(upcoming);
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(doses);
        ArgumentNullException.ThrowIfNull(meals);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var lines = new List<string>();
        if (Checks(readings, today) is { } checks)
        {
            lines.Add(checks);
        }

        if (doses.OrderByDescending(dose => dose.OnDay).FirstOrDefault() is { } latest)
        {
            lines.Add($"Dose on file since {Day(latest.OnDay)}: “{latest.Dose}”.");
        }

        if (VitaminK(meals, now) is { } vitaminK)
        {
            lines.Add(vitaminK);
        }

        lines.AddRange(Trips(upcoming, now));
        return lines;
    }

    /// <summary>Slice 3: a trip in the next two weeks, so a check can be arranged before it.</summary>
    private static IEnumerable<string> Trips(IReadOnlyList<CalendarEvent> upcoming, DateTimeOffset now) =>
        upcoming
            .Where(item => item.StartsAt > now && item.StartsAt <= now.AddDays(14) && Travel().IsMatch(item.Summary))
            .OrderBy(item => item.StartsAt)
            .Take(2)
            .Select(item => $"Trip coming up: “{item.Summary}” on {Day(DateOnly.FromDateTime(item.StartsAt.UtcDateTime))}. "
                + "If an INR check would fall while you're away, your clinic may want one before you go.");

    [GeneratedRegex(@"\b(?:flight|fly|flying|trip|travel|vacation|holiday|hotel|airport|cruise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Travel();

    private static string? Checks(IReadOnlyList<InrReading> readings, DateOnly today)
    {
        var days = readings.GroupBy(reading => reading.OnDay).OrderByDescending(day => day.Key).ToList();
        if (days.Count == 0)
        {
            return null;
        }

        var last = days[0].First();
        var elapsed = today.DayNumber - last.OnDay.DayNumber;
        var line = $"Last INR {last.Inr.ToString("0.0", CultureInfo.InvariantCulture)} on {Day(last.OnDay)}, {elapsed} days ago";
        if (days.Count < MINIMUM_READINGS)
        {
            return line + ".";
        }

        var gaps = days.Zip(days.Skip(1), (later, earlier) => later.Key.DayNumber - earlier.Key.DayNumber).Order().ToList();
        var usual = gaps[gaps.Count / 2];
        return elapsed >= usual * OVERDUE_FACTOR && elapsed >= 7
            ? $"{line} — longer than your usual {usual} days between checks, so a check may be due."
            : line + ".";
    }

    private static string? VitaminK(IReadOnlyList<Meal> meals, DateTimeOffset now)
    {
        var classified = meals.Where(meal => meal.VitaminK is not null).ToList();
        var thisWeek = classified.Count(meal => meal.EatenAt > now.AddDays(-7) && meal.VitaminK == "high");
        if (classified.All(meal => meal.EatenAt <= now.AddDays(-7)))
        {
            return null;
        }

        if (classified.Min(meal => meal.EatenAt) > now.AddDays(-21))
        {
            return $"{thisWeek} high-vitamin-K meals this week; not enough history yet to know your usual.";
        }

        var usual = (double)classified.Count(meal => meal.VitaminK == "high"
            && meal.EatenAt <= now.AddDays(-7) && meal.EatenAt > now.AddDays(-7 - (7 * USUAL_WEEKS))) / USUAL_WEEKS;
        return Math.Abs(thisWeek - usual) >= 2
            ? $"{thisWeek} high-vitamin-K meals this week, against about {Math.Round(usual)} a week before. It's steady intake "
              + "that keeps INR steady; if your eating really changed, your clinic may want an earlier check."
            : null;
    }

    private static string Day(DateOnly day) => day.ToString("MMM d", CultureInfo.InvariantCulture);
}
