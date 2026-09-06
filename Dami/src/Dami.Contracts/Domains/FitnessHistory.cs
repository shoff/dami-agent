using System.Globalization;

namespace Dami.Contracts.Domains;

/// <summary>What the log already holds on one exercise, in words to compare today against. Pure.</summary>
/// <remarks>
/// On 2026-09-06 the frontier logged a set and said "try 115 lb next time" with no history in
/// hand: the exercise had been minted under a new name beside seven existing curl entries,
/// and the tool result carried nothing to compare. This is what a coach reads before
/// speaking — the known names so a new entry lands on the old one, and the last few
/// sessions with the best, so the comparison is real numbers rather than an invented one.
/// </remarks>
public static class FitnessHistory
{
    private const int SESSIONS = 3;

    /// <summary>Every exercise name in the log, most recently trained first, in the log's own spelling.</summary>
    public static IReadOnlyList<string> KnownExercises(FitnessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Sets
            .GroupBy(set => set.Exercise, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(set => set.OccurredAt))
            .Select(group => group.OrderByDescending(set => set.OccurredAt).First().Exercise)
            .ToList();
    }

    /// <summary>The log's own spelling of a name when it already has one, else the name as given.</summary>
    public static string Resolve(IReadOnlyList<string> known, string requested)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        var wanted = Squash(requested);
        return known.FirstOrDefault(name => Squash(name) == wanted) ?? requested.Trim();
    }

    /// <summary>
    /// The last few sessions on the exercise other than <paramref name="exceptEvent"/> (the one
    /// just written), newest first, and the best working set before it.
    /// </summary>
    public static string Describe(FitnessSnapshot snapshot, string exercise, Guid exceptEvent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(exercise);
        var sessions = snapshot.Sets
            .Where(set => string.Equals(set.Exercise, exercise, StringComparison.OrdinalIgnoreCase))
            .Where(set => set.FitnessEventId != exceptEvent && !set.IsWarmup)
            .GroupBy(set => set.FitnessEventId)
            .OrderByDescending(group => group.Max(set => set.OccurredAt))
            .ToList();
        if (sessions.Count == 0)
        {
            return $"First time on {exercise} in the log.";
        }

        var recent = string.Join("; ", sessions.Take(SESSIONS).Select(Summary));
        return $"Before today on {exercise} ({sessions.Count} session(s)): {recent}.{Best(sessions)}";
    }

    private static string Best(IEnumerable<IGrouping<Guid, FitnessSet>> sessions)
    {
        var best = sessions.SelectMany(session => session)
            .Select(set => (Set: set, Max: FitnessInsights.EstimatedOneRepMax(set)))
            .Where(pair => pair.Max is not null)
            .OrderByDescending(pair => pair.Max)
            .Select(pair => pair.Set)
            .FirstOrDefault();
        return best is null
            ? string.Empty
            : $" Best before today: {Weight(best.WeightLbs!.Value)} lb × {best.Reps} on {Date(best.OccurredAt)}.";
    }

    private static string Summary(IGrouping<Guid, FitnessSet> session)
    {
        var sets = session.OrderBy(set => set.SetNumber).ToList();
        var reps = sets.Where(set => set.Reps is not null).Select(set => set.Reps!.Value).ToList();
        var repText = reps.Count == 0 ? string.Empty
            : reps.Min() == reps.Max() ? $"x{reps.Min()}" : $"x{reps.Min()}-{reps.Max()}";
        var weight = sets.Max(set => set.WeightLbs) is { } top ? $" at {Weight(top)} lb" : string.Empty;
        var rpe = sets.Max(set => set.Rpe) is { } hardest ? $" RPE {hardest}" : string.Empty;
        return $"{Date(sets[0].OccurredAt)}: {sets.Count}{repText}{weight}{rpe}";
    }

    private static string Squash(string name) =>
        string.Join(' ', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Weight(decimal pounds) => pounds.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset at) => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
