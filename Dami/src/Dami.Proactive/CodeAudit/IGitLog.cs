namespace Dami.Proactive.CodeAudit;

/// <summary>Read-only view of a git repository's recent changes.</summary>
public interface IGitLog
{
    /// <summary>The patch text of commits from the last <paramref name="window"/>. Empty when quiet.</summary>
    Task<string> RecentPatchAsync(string repoPath, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>The committer times of every non-merge commit since a moment, newest first.</summary>
    Task<IReadOnlyList<DateTimeOffset>> CommitTimesAsync(string repoPath, DateTimeOffset since, CancellationToken cancellationToken);
}
