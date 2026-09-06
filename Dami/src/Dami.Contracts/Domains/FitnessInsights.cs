using System.Globalization;

namespace Dami.Contracts.Domains;

/// <summary>What kind of thing the log noticed.</summary>
public enum FitnessInsightKind
{
    /// <summary>The best ever on an exercise, just now.</summary>
    PersonalRecord,

    /// <summary>An exercise that has not moved in six weeks.</summary>
    Plateau,

    /// <summary>A muscle group trained lately but not in the last two weeks.</summary>
    Neglected,

    /// <summary>What to load next time, from the last session's RPE.</summary>
    Suggestion,

    /// <summary>The week against the one before.</summary>
    WeeklySummary,
}

/// <summary>One thing worth saying, with the numbers that justify it.</summary>
public sealed record FitnessInsight(FitnessInsightKind Kind, string Exercise, string Text);

/// <summary>Reads the log and says what a good coach would notice. Pure.</summary>
/// <remarks>
/// Estimated one-rep max is Epley (weight × (1 + reps/30)) on working sets, which is good
/// enough to compare a lifter with himself across rep ranges. Every sentence carries the
/// numbers it came from, because the point is not the sentence but that Steve can check it.
/// </remarks>
public static class FitnessInsights
{
    private const int PLATEAU_SESSIONS = 4;
    private const int PLATEAU_WINDOW_DAYS = 42;
    private const int PLATEAU_HISTORY_DAYS = 84;
    private const double PLATEAU_TOLERANCE = 1.025;
    private const int NEGLECT_DAYS = 14;
    private const int NEGLECT_HISTORY_DAYS = 90;
    private const decimal STEP_LBS = 5m;

    /// <summary>Everything the log supports saying as of <paramref name="now"/>.</summary>
    public static IReadOnlyList<FitnessInsight> Analyze(FitnessSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sessions = Sessions(snapshot.Sets);
        var insights = new List<FitnessInsight>();
        insights.AddRange(PersonalRecords(sessions, now));
        insights.AddRange(Plateaus(sessions, now));
        insights.AddRange(Neglected(snapshot.Sets, now));
        insights.AddRange(Suggestions(sessions, now));
        if (WeeklySummary(snapshot, now) is { } week)
        {
            insights.Add(week);
        }

        return insights;
    }

    /// <summary>Only what concerns one exercise: what to say right after logging it.</summary>
    public static IReadOnlyList<FitnessInsight> ForExercise(FitnessSnapshot snapshot, string exercise, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        return Analyze(snapshot, now)
            .Where(insight => string.Equals(insight.Exercise, exercise, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Epley's estimate, on a working set with a weight.</summary>
    public static double? EstimatedOneRepMax(FitnessSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.WeightLbs is { } weight && set.Reps is { } reps && reps > 0 && !set.IsWarmup
            ? (double)weight * (1 + reps / 30.0)
            : null;
    }

    private sealed record Session(string Exercise, DateTimeOffset At, double Best, FitnessSet BestSet, IReadOnlyList<FitnessSet> Sets);

    private static List<Session> Sessions(IReadOnlyList<FitnessSet> sets) =>
        sets.GroupBy(set => (set.Exercise, set.FitnessEventId))
            .Select(group =>
            {
                var scored = group.Select(set => (Set: set, Max: EstimatedOneRepMax(set))).Where(pair => pair.Max is not null).ToList();
                return scored.Count == 0
                    ? null
                    : new Session(group.Key.Exercise, group.Max(set => set.OccurredAt), scored.Max(pair => pair.Max!.Value),
                        scored.OrderByDescending(pair => pair.Max).First().Set, group.ToList());
            })
            .Where(session => session is not null)
            .Select(session => session!)
            .OrderBy(session => session.At)
            .ToList();

    private static IEnumerable<FitnessInsight> PersonalRecords(List<Session> sessions, DateTimeOffset now)
    {
        foreach (var byExercise in sessions.GroupBy(session => session.Exercise, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = byExercise.OrderBy(session => session.At).ToList();
            var latest = ordered[^1];
            if (ordered.Count < 2 || (now - latest.At) > TimeSpan.FromDays(2))
            {
                continue;
            }

            var previous = ordered[..^1].OrderByDescending(session => session.Best).First();
            if (latest.Best > previous.Best)
            {
                yield return new FitnessInsight(FitnessInsightKind.PersonalRecord, latest.Exercise,
                    $"New best on {latest.Exercise}: {Lbs(latest.BestSet.WeightLbs)} × {latest.BestSet.Reps} (est. 1RM {latest.Best:0}), "
                    + $"up from {Lbs(previous.BestSet.WeightLbs)} × {previous.BestSet.Reps} on {previous.At:MMM d}.");
            }
        }
    }

    private static IEnumerable<FitnessInsight> Plateaus(List<Session> sessions, DateTimeOffset now)
    {
        foreach (var byExercise in sessions.GroupBy(session => session.Exercise, StringComparer.OrdinalIgnoreCase))
        {
            var recent = byExercise.Where(session => session.At > now.AddDays(-PLATEAU_HISTORY_DAYS)).OrderBy(session => session.At).ToList();
            if (recent.Count < PLATEAU_SESSIONS)
            {
                continue;
            }

            var window = recent.Where(session => session.At > now.AddDays(-PLATEAU_WINDOW_DAYS)).ToList();
            var before = recent.Where(session => session.At <= now.AddDays(-PLATEAU_WINDOW_DAYS)).ToList();
            if (window.Count < 2 || before.Count == 0)
            {
                continue;
            }

            var earlier = before.Max(session => session.Best);
            if (window.Max(session => session.Best) <= earlier * PLATEAU_TOLERANCE)
            {
                yield return new FitnessInsight(FitnessInsightKind.Plateau, byExercise.Key,
                    $"{byExercise.Key} has been flat for six weeks: best est. 1RM {earlier:0} on {before.OrderByDescending(s => s.Best).First().At:MMM d}, "
                    + $"{window.Max(session => session.Best):0} since, over {window.Count} sessions.");
            }
        }
    }

    private static IEnumerable<FitnessInsight> Neglected(IReadOnlyList<FitnessSet> sets, DateTimeOffset now)
    {
        var byGroup = sets.Where(set => !string.IsNullOrWhiteSpace(set.MuscleGroup))
            .GroupBy(set => set.MuscleGroup!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byGroup)
        {
            var days = group.Select(set => set.OccurredAt.Date).Distinct().ToList();
            var recentSessions = days.Count(day => day > now.AddDays(-NEGLECT_HISTORY_DAYS).Date);
            var last = days.Max();
            if (recentSessions >= 2 && last <= now.AddDays(-NEGLECT_DAYS).Date)
            {
                yield return new FitnessInsight(FitnessInsightKind.Neglected, group.Key,
                    $"No {group.Key} work since {last:MMM d} ({(now.Date - last).Days} days), after {recentSessions} sessions in the 90 days before.");
            }
        }
    }

    private static IEnumerable<FitnessInsight> Suggestions(List<Session> sessions, DateTimeOffset now)
    {
        foreach (var latest in sessions.GroupBy(session => session.Exercise, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(session => session.At).Last())
            .Where(session => (now - session.At) <= TimeSpan.FromDays(2)))
        {
            var working = latest.Sets.Where(set => !set.IsWarmup && set.Rpe is not null && set.WeightLbs is not null).ToList();
            if (working.Count == 0)
            {
                continue;
            }

            var weight = working.Max(set => set.WeightLbs!.Value);
            var hardest = working.Max(set => set.Rpe!.Value);
            var text = hardest <= 7
                ? $"{latest.Exercise}: every set was RPE {hardest} or easier at {Lbs(weight)} — try {Lbs(weight + STEP_LBS)} next time."
                : hardest >= 9
                    ? $"{latest.Exercise}: RPE {hardest} at {Lbs(weight)} — hold the weight and get the reps back next time."
                    : null;
            if (text is not null)
            {
                yield return new FitnessInsight(FitnessInsightKind.Suggestion, latest.Exercise, text);
            }
        }
    }

    private static FitnessInsight? WeeklySummary(FitnessSnapshot snapshot, DateTimeOffset now)
    {
        var week = Week(snapshot, now.AddDays(-7), now);
        var before = Week(snapshot, now.AddDays(-14), now.AddDays(-7));
        if (week.Sessions == 0 && before.Sessions == 0)
        {
            return null;
        }

        return new FitnessInsight(FitnessInsightKind.WeeklySummary, string.Empty,
            $"This week: {week.Sessions} lifting session(s), {week.Sets} sets, {week.Volume:N0} lb moved, {week.CardioMinutes} min cardio. "
            + $"Last week: {before.Sessions}, {before.Sets} sets, {before.Volume:N0} lb, {before.CardioMinutes} min.");
    }

    private static (int Sessions, int Sets, double Volume, int CardioMinutes) Week(FitnessSnapshot snapshot, DateTimeOffset from, DateTimeOffset to)
    {
        var sets = snapshot.Sets.Where(set => set.OccurredAt > from && set.OccurredAt <= to).ToList();
        var cardio = snapshot.Cardio.Where(session => session.OccurredAt > from && session.OccurredAt <= to).Sum(session => session.DurationSeconds ?? 0);
        return (
            sets.Select(set => set.FitnessEventId).Distinct().Count(),
            sets.Count,
            sets.Sum(set => (double)(set.WeightLbs ?? 0) * (set.Reps ?? 0)),
            cardio / 60);
    }

    private static string Lbs(decimal? weight) =>
        weight is { } value ? value.ToString("0.#", CultureInfo.InvariantCulture) + " lb" : "bodyweight";
}
