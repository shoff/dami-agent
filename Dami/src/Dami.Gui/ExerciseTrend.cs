using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>The best work done on one exercise on one day.</summary>
public sealed record ExerciseDay(
    DateTimeOffset Day, double TopWeight, int RepsAtTop, double Estimated1Rm);

/// <summary>Per-exercise progression, from working sets only. Pure.</summary>
public static class ExerciseTrend
{
    /// <summary>Epley's estimate: the common currency for comparing 5×185 to 8×170.</summary>
    /// <remarks>
    /// An estimate, not a claim — nobody tested the single. But without it a rep-range
    /// change reads as regression, and every real progression through rep ranges
    /// disappears from the chart.
    /// </remarks>
    public static double Estimate1Rm(double weightLbs, int reps) =>
        reps <= 1 ? weightLbs : weightLbs * (1 + reps / 30.0);

    /// <summary>Each training day's best set for the exercise, oldest first.</summary>
    public static IReadOnlyList<ExerciseDay> Days(IReadOnlyList<FitnessSet> sets, string exercise)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentException.ThrowIfNullOrWhiteSpace(exercise);

        return sets
            .Where(set => set.Exercise == exercise && !set.IsWarmup
                && set is { WeightLbs: not null, Reps: not null })
            .GroupBy(set => DateOnly.FromDateTime(set.OccurredAt.UtcDateTime))
            .Select(day => day
                .Select(set => new ExerciseDay(
                    day.Min(inner => inner.OccurredAt),
                    (double)set.WeightLbs!.Value,
                    set.Reps!.Value,
                    Estimate1Rm((double)set.WeightLbs.Value, set.Reps.Value)))
                .MaxBy(candidate => candidate.Estimated1Rm)!)
            .OrderBy(day => day.Day)
            .ToList();
    }
}
