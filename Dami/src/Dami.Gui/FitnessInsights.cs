using System.Globalization;
using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>One deterministic observation about the training data.</summary>
public sealed record FitnessInsight(string Kind, string Text, string Detail);

/// <summary>The suggestive half of the dashboard: arithmetic over the rows, nothing more.</summary>
/// <remarks>
/// Every suggestion here is a deterministic computation the reader can check against the
/// charts beside it — no model, no claimed judgment (the UI shows evidence, not
/// chain-of-thought). Thresholds are habit-relative where possible: nine days without
/// cardio is news for someone who runs daily and silence for someone who runs weekly.
/// Scarce by intent (D-021): an ordinary rest day must produce nothing.
/// </remarks>
public static class FitnessInsights
{
    private const int RECENT_DAYS = 28;
    private const double STEADY_LB_PER_WEEK = 0.25;

    /// <summary>Builds every insight the data supports right now.</summary>
    public static List<FitnessInsight> Build(FitnessSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var insights = new List<FitnessInsight>();
        AddWeightTrend(insights, snapshot.WeighIns, now);
        AddGap(insights, "cardio", SessionDays(snapshot.Cardio.Select(session => session.OccurredAt)), now);
        AddGap(insights, "resistance", SessionDays(snapshot.Sets.Select(set => set.OccurredAt)), now);
        AddMuscleBalance(insights, snapshot.Sets, now);
        AddLiftChanges(insights, snapshot.Sets);
        AddStreak(insights, snapshot, now);
        return insights;
    }

    private static void AddWeightTrend(
        List<FitnessInsight> insights, IReadOnlyList<FitnessWeighIn> weighIns, DateTimeOffset now)
    {
        var window = weighIns.Where(weighIn => (now - weighIn.OccurredAt).TotalDays <= 35).ToList();
        if (window.Count < 3)
        {
            return;
        }

        var perWeek = SlopePerDay(window, now) * 7;
        var text = Math.Abs(perWeek) < STEADY_LB_PER_WEEK
            ? Invariant($"Body weight steady around {window.Average(weighIn => weighIn.WeightLbs):F0} lb")
            : Invariant($"Body weight trending {(perWeek < 0 ? "down" : "up")} {Math.Abs(perWeek):F1} lb/week");
        insights.Add(new FitnessInsight(
            "weight", text, Invariant($"{window.Count} weigh-ins over the last 5 weeks")));
    }

    private static void AddGap(
        List<FitnessInsight> insights, string kind, IReadOnlyList<DateOnly> days, DateTimeOffset now)
    {
        if (days.Count < 5)
        {
            return;
        }

        var gaps = new List<double>();
        for (var index = 1; index < days.Count; index++)
        {
            gaps.Add(days[index].DayNumber - days[index - 1].DayNumber);
        }

        var usual = Median(gaps);
        var since = DateOnly.FromDateTime(now.UtcDateTime).DayNumber - days[^1].DayNumber;
        if (since > Math.Max(2 * usual, usual + 2))
        {
            insights.Add(new FitnessInsight(
                "gap",
                Invariant($"{since} days since {kind}"),
                Invariant($"your usual gap is {usual:F0} day(s)")));
        }
    }

    private static void AddMuscleBalance(
        List<FitnessInsight> insights, IReadOnlyList<FitnessSet> sets, DateTimeOffset now)
    {
        var trained = sets
            .Where(set => set.MuscleGroup is not null && !set.IsWarmup)
            .GroupBy(set => set.MuscleGroup!)
            .Where(group => group.Count() >= 10);
        foreach (var group in trained)
        {
            if (!group.Any(set => (now - set.OccurredAt).TotalDays <= RECENT_DAYS))
            {
                insights.Add(new FitnessInsight(
                    "balance",
                    Invariant($"No {group.Key} work in the last 4 weeks"),
                    Invariant($"{group.Count()} sets on record")));
            }
        }
    }

    private static void AddLiftChanges(List<FitnessInsight> insights, IReadOnlyList<FitnessSet> sets)
    {
        var changes = sets
            .Select(set => set.Exercise)
            .Distinct()
            .Select(exercise => (Exercise: exercise, Days: ExerciseTrend.Days(sets, exercise)))
            .Where(lift => lift.Days.Count >= 6)
            .Select(lift => (lift.Exercise, lift.Days, Delta: Delta(lift.Days)))
            .ToList();

        var movers = changes.Where(lift => lift.Delta >= 2.5).ToList();
        if (movers.Count > 0)
        {
            var mover = movers.MaxBy(lift => lift.Delta);
            insights.Add(new FitnessInsight(
                "lift",
                Invariant($"{mover.Exercise} trending up — +{mover.Delta:F0} lb est. 1RM"),
                Invariant($"best recent set {mover.Days[^1].TopWeight:F0} lb × {mover.Days[^1].RepsAtTop}")));
        }

        var flats = changes.Where(lift => lift.Delta <= 0).ToList();
        if (flats.Count > 0)
        {
            var flat = flats.MinBy(lift => lift.Delta);
            insights.Add(new FitnessInsight(
                "lift",
                Invariant($"{flat.Exercise} has been flat for {Math.Min(flat.Days.Count, 6)} sessions"),
                "same estimated 1RM — maybe change the progression"));
        }
    }

    private static void AddStreak(
        List<FitnessInsight> insights, FitnessSnapshot snapshot, DateTimeOffset now)
    {
        var days = SessionDays(snapshot.Cardio.Select(session => session.OccurredAt)
            .Concat(snapshot.Sets.Select(set => set.OccurredAt)));
        var streak = 0;

        // The current, partial week neither counts nor breaks the run.
        for (var block = 1; block < 52; block++)
        {
            var start = now.AddDays(-7 * (block + 1));
            var end = now.AddDays(-7 * block);
            if (days.Count(day => InBlock(day, start, end)) < 2)
            {
                break;
            }

            streak++;
        }

        if (streak >= 3)
        {
            insights.Add(new FitnessInsight(
                "streak", Invariant($"{streak}-week streak of 2+ sessions"), "keep it going"));
        }
    }

    /// <summary>Best estimated 1RM of the last three training days against the three before.</summary>
    private static double Delta(IReadOnlyList<ExerciseDay> days)
    {
        var recent = days.Skip(days.Count - 3).Max(day => day.Estimated1Rm);
        var prior = days.Skip(days.Count - 6).Take(3).Max(day => day.Estimated1Rm);
        return recent - prior;
    }

    /// <summary>Least-squares slope in lb per day, over days-before-now.</summary>
    private static double SlopePerDay(IReadOnlyList<FitnessWeighIn> weighIns, DateTimeOffset now)
    {
        var points = weighIns
            .Select(weighIn => (X: (weighIn.OccurredAt - now).TotalDays, Y: (double)weighIn.WeightLbs))
            .ToList();
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        var covariance = points.Sum(point => (point.X - meanX) * (point.Y - meanY));
        var variance = points.Sum(point => (point.X - meanX) * (point.X - meanX));
        return variance == 0 ? 0 : covariance / variance;
    }

    private static bool InBlock(DateOnly day, DateTimeOffset start, DateTimeOffset end)
    {
        var at = day.DayNumber;
        return at > DateOnly.FromDateTime(start.UtcDateTime).DayNumber
            && at <= DateOnly.FromDateTime(end.UtcDateTime).DayNumber;
    }

    private static List<DateOnly> SessionDays(IEnumerable<DateTimeOffset> occurrences) =>
        occurrences
            .Select(at => DateOnly.FromDateTime(at.UtcDateTime))
            .Distinct()
            .OrderBy(day => day)
            .ToList();

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
