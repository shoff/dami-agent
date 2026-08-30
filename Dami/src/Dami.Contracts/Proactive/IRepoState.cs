namespace Dami.Contracts.Proactive;

/// <summary>What a working copy looks like right now, from git's own answers.</summary>
public sealed record RepoState(
    bool IsRepository,
    string Branch,
    bool HasUpstream,
    int Ahead,
    int Behind,
    DateTimeOffset? OldestUnpushedAt,
    IReadOnlyList<string> DirtyPaths,
    DateTimeOffset? LastFetchAt,
    IReadOnlyList<string> TrackedDdlFiles)
{
    /// <summary>A repository that could not be read, so nothing can be concluded from it.</summary>
    public static readonly RepoState unknown =
        new(false, string.Empty, false, 0, 0, null, [], null, []);
}

/// <summary>Reads a working copy without changing it.</summary>
/// <remarks>
/// Read-only by construction: this exists to notice that work is stranded, never to move
/// it. Committing and pushing on someone's behalf is exactly the kind of helpfulness that
/// turns a watcher into a liability, and a nightly job that pushes is a nightly job that
/// pushes something half-finished.
/// </remarks>
public interface IRepoState
{
    /// <summary>Reads the current state of the working copy at <paramref name="repoPath"/>.</summary>
    Task<RepoState> ReadAsync(string repoPath, CancellationToken cancellationToken);
}

/// <summary>The migrations a database says it has applied.</summary>
/// <remarks>
/// Kept separate from the repository read because the whole point is comparing two
/// independent sources: what the database believes and what the tree contains. Asking one
/// of them for both answers would defeat it.
/// </remarks>
public interface ISchemaLedger
{
    /// <summary>Filenames recorded in the migration ledger, in order.</summary>
    Task<IReadOnlyList<string>> AppliedAsync(CancellationToken cancellationToken);
}
