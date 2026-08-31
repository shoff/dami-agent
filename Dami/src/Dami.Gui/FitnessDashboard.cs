using System.Globalization;
using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>One headline number.</summary>
public sealed record FitnessTile(string Label, string Value, string Detail);

/// <summary>One recent session, rendered for the list.</summary>
public sealed record FitnessSessionRow(string When, string Title, string Detail);

/// <summary>One pickable exercise.</summary>
public sealed record FitnessExerciseChoice(string Name, string Label);

/// <summary>Shapes the snapshot into tiles, lists, and choices. Pure.</summary>
public static class FitnessDashboard
{
    private const int WEEK_DAYS = 7;

    /// <summary>The headline numbers: weight, sessions, tonnage, cardio.</summary>
    public static List<FitnessTile> Tiles(FitnessSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tiles = new List<FitnessTile>();
        AddWeightTile(tiles, snapshot.WeighIns, now);
        AddSessionsTile(tiles, snapshot, now);
        AddTonnageTile(tiles, snapshot.Sets, now);
        AddCardioTile(tiles, snapshot.Cardio, now);
        return tiles;
    }

    /// <summary>Distinct exercises, most training days first.</summary>
    public static List<FitnessExerciseChoice> Exercises(IReadOnlyList<FitnessSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return sets
            .Where(set => !set.IsWarmup)
            .GroupBy(set => set.Exercise)
            .Select(group => (
                Name: group.Key,
                Days: group.Select(set => DateOnly.FromDateTime(set.OccurredAt.UtcDateTime))
                    .Distinct().Count()))
            .OrderByDescending(choice => choice.Days)
            .ThenBy(choice => choice.Name, StringComparer.Ordinal)
            .Select(choice => new FitnessExerciseChoice(
                choice.Name,
                Invariant($"{choice.Name}  ·  {choice.Days} day(s)")))
            .ToList();
    }

    /// <summary>Cardio, lifting days, and weigh-ins interleaved, newest first.</summary>
    public static List<FitnessSessionRow> RecentSessions(FitnessSnapshot snapshot, int limit)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return snapshot.Cardio.Select(CardioRow)
            .Concat(LiftingRows(snapshot.Sets))
            .Concat(snapshot.WeighIns.Select(weighIn => (weighIn.OccurredAt, Row: new FitnessSessionRow(
                When(weighIn.OccurredAt), "weigh-in", Invariant($"{weighIn.WeightLbs:F1} lb")))))
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(limit)
            .Select(entry => entry.Row)
            .ToList();
    }

    private static (DateTimeOffset OccurredAt, FitnessSessionRow Row) CardioRow(
        FitnessCardioSession session)
    {
        var minutes = session.DurationSeconds is { } seconds ? seconds / 60 : 0;
        var extras = new List<string>();
        if (session.DistanceMi is { } miles)
        {
            extras.Add(Invariant($"{miles:F1} mi"));
        }

        if (session.HrAvg is { } hr)
        {
            extras.Add(Invariant($"avg HR {hr}"));
        }

        return (session.OccurredAt, new FitnessSessionRow(
            When(session.OccurredAt),
            Invariant($"{session.Modality} · {minutes} min"),
            string.Join(" · ", extras)));
    }

    private static IEnumerable<(DateTimeOffset OccurredAt, FitnessSessionRow Row)> LiftingRows(
        IReadOnlyList<FitnessSet> sets)
    {
        return sets
            .GroupBy(set => DateOnly.FromDateTime(set.OccurredAt.UtcDateTime))
            .Select(day =>
            {
                var at = day.Min(set => set.OccurredAt);
                var top = day.Where(set => set is { IsWarmup: false, WeightLbs: not null })
                    .MaxBy(set => set.WeightLbs);
                var detail = top is null
                    ? Invariant($"{day.Count()} sets")
                    : Invariant($"{day.Count()} sets · top {top.Exercise} {top.Reps}×{top.WeightLbs:F0}");
                return (at, new FitnessSessionRow(When(at), "resistance", detail));
            });
    }

    private static void AddWeightTile(
        List<FitnessTile> tiles, IReadOnlyList<FitnessWeighIn> weighIns, DateTimeOffset now)
    {
        if (weighIns.Count == 0)
        {
            return;
        }

        var latest = weighIns[^1];
        var monthAgo = weighIns.LastOrDefault(
            weighIn => (now - weighIn.OccurredAt).TotalDays >= 28);
        var detail = monthAgo is null
            ? When(latest.OccurredAt)
            : Invariant($"{latest.WeightLbs - monthAgo.WeightLbs:+0.0;-0.0;±0.0} lb vs a month ago");
        tiles.Add(new FitnessTile(
            "BODY WEIGHT", Invariant($"{latest.WeightLbs:F1} lb"), detail));
    }

    private static void AddSessionsTile(
        List<FitnessTile> tiles, FitnessSnapshot snapshot, DateTimeOffset now)
    {
        var days = snapshot.Cardio.Select(session => session.OccurredAt)
            .Concat(snapshot.Sets.Select(set => set.OccurredAt))
            .ToList();
        if (days.Count == 0)
        {
            return;
        }

        var thisWeek = CountSessions(days, now, 0);
        var lastWeek = CountSessions(days, now, 1);
        tiles.Add(new FitnessTile(
            "SESSIONS · 7 DAYS",
            Invariant($"{thisWeek}"),
            Invariant($"{lastWeek} the week before")));
    }

    private static void AddTonnageTile(
        List<FitnessTile> tiles, IReadOnlyList<FitnessSet> sets, DateTimeOffset now)
    {
        if (sets.Count == 0)
        {
            return;
        }

        var totals = FitnessCharts.WeeklyTonnage(sets, now, weeks: 2);
        tiles.Add(new FitnessTile(
            "TONNAGE · 7 DAYS",
            Invariant($"{totals[^1]:N0} lb"),
            Invariant($"{totals[0]:N0} lb the week before")));
    }

    private static void AddCardioTile(
        List<FitnessTile> tiles, IReadOnlyList<FitnessCardioSession> cardio, DateTimeOffset now)
    {
        if (cardio.Count == 0)
        {
            return;
        }

        var totals = FitnessCharts.WeeklyCardioMinutes(cardio, now, weeks: 2);
        tiles.Add(new FitnessTile(
            "CARDIO · 7 DAYS",
            Invariant($"{totals[^1]:N0} min"),
            Invariant($"{totals[0]:N0} min the week before")));
    }

    /// <summary>Distinct training days in the trailing 7-day block <paramref name="blocksAgo"/>.</summary>
    private static int CountSessions(
        IReadOnlyList<DateTimeOffset> occurrences, DateTimeOffset now, int blocksAgo)
    {
        return occurrences
            .Where(at =>
            {
                var daysAgo = (now - at).TotalDays;
                return daysAgo >= blocksAgo * WEEK_DAYS && daysAgo < (blocksAgo + 1) * WEEK_DAYS;
            })
            .Select(at => DateOnly.FromDateTime(at.UtcDateTime))
            .Distinct()
            .Count();
    }

    private static string When(DateTimeOffset at) =>
        at.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
