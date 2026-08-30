using Dami.Contracts.Proactive;

namespace Dami.Proactive.Hygiene;

/// <summary>How much drift is tolerated before it is worth saying.</summary>
public sealed class RepoHygieneOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "RepoHygiene";

    /// <summary>The working copy to watch.</summary>
    public string RepositoryPath { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>Unpushed commits allowed before it is worth mentioning.</summary>
    /// <remarks>
    /// One is normal mid-session. The condition worth naming is work accumulating, so this
    /// is a count *and* an age below: a single commit stranded for a week matters and
    /// twenty from this afternoon do not.
    /// </remarks>
    public int AheadThreshold { get; set; } = 5;

    /// <summary>How long an unpushed commit may sit before its age alone is the point.</summary>
    public TimeSpan UnpushedAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long the remote may go unexamined before the ahead count is untrustworthy.</summary>
    public TimeSpan FetchAge { get; set; } = TimeSpan.FromDays(2);
}

/// <summary>Decides what, if anything, is worth saying about a working copy.</summary>
/// <remarks>
/// Pure, so the thresholds can be argued with in tests rather than discovered in
/// production. Every rule here earned its place from something that actually happened on
/// this host, and the whole thing is written to stay silent on an ordinary day — a
/// watcher that cries most nights is one that gets muted, and then it is not a watcher.
/// </remarks>
public static class RepoHygiene
{
    /// <summary>Everything adrift, worst first. Empty when the working copy is fine.</summary>
    public static IReadOnlyList<string> Assess(
        RepoState state,
        IReadOnlyList<string> appliedMigrations,
        DateTimeOffset now,
        RepoHygieneOptions options)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(appliedMigrations);
        ArgumentNullException.ThrowIfNull(options);

        var findings = new List<string>();
        AddMigrationDrift(findings, state, appliedMigrations);
        AddUnpushed(findings, state, now, options);
        AddStaleFetch(findings, state, now, options);
        AddDirty(findings, state);
        return findings;
    }

    /// <remarks>
    /// First, because it is the only one that means two systems disagree about reality
    /// rather than merely that work is late. A migration applied to the database whose
    /// file is untracked cannot be reviewed, cannot be checksummed by apply.sh, and does
    /// not exist for anyone who clones the repository.
    /// </remarks>
    private static void AddMigrationDrift(
        List<string> findings,
        RepoState state,
        IReadOnlyList<string> applied)
    {
        var tracked = state.TrackedDdlFiles.ToHashSet(StringComparer.Ordinal);
        var missing = applied.Where(file => !tracked.Contains(file)).ToList();
        if (missing.Count > 0)
        {
            findings.Add(
                $"{missing.Count} migration(s) applied to the database are not tracked in git: "
                + string.Join(", ", missing));
        }
    }

    private static void AddUnpushed(
        List<string> findings,
        RepoState state,
        DateTimeOffset now,
        RepoHygieneOptions options)
    {
        if (!state.HasUpstream)
        {
            findings.Add($"branch {state.Branch} has no upstream, so nothing it holds is pushed anywhere");
            return;
        }

        if (state.Ahead == 0)
        {
            return;
        }

        var age = state.OldestUnpushedAt is { } oldest ? now - oldest : TimeSpan.Zero;
        if (state.Ahead >= options.AheadThreshold || age >= options.UnpushedAge)
        {
            findings.Add(
                $"{state.Ahead} commit(s) on {state.Branch} are not pushed"
                + (age > TimeSpan.Zero ? $"; the oldest has waited {Describe(age)}" : string.Empty));
        }
    }

    /// <remarks>
    /// A stale fetch is not itself a problem — it makes the ahead and behind counts above
    /// stale too, which is worse than not knowing, because they still look like answers.
    /// </remarks>
    private static void AddStaleFetch(
        List<string> findings,
        RepoState state,
        DateTimeOffset now,
        RepoHygieneOptions options)
    {
        if (state.LastFetchAt is not { } fetched)
        {
            findings.Add("the remote has never been fetched, so the ahead count above means little");
            return;
        }

        var age = now - fetched;
        if (age >= options.FetchAge)
        {
            findings.Add($"the remote has not been fetched for {Describe(age)}");
        }
    }

    private static void AddDirty(List<string> findings, RepoState state)
    {
        if (state.DirtyPaths.Count == 0)
        {
            return;
        }

        var shown = state.DirtyPaths.Take(5);
        var rest = state.DirtyPaths.Count - 5;
        findings.Add(
            $"{state.DirtyPaths.Count} uncommitted path(s): {string.Join(", ", shown)}"
            + (rest > 0 ? $", and {rest} more" : string.Empty));
    }

    private static string Describe(TimeSpan age)
    {
        return age.TotalDays >= 1
            ? $"{age.TotalDays:0} day(s)"
            : $"{age.TotalHours:0} hour(s)";
    }
}
