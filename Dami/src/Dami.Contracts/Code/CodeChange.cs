namespace Dami.Contracts.Code;

/// <summary>What one code task produced.</summary>
/// <param name="Branch">The branch the work is on, e.g. <c>dami/20260916-1830-fix-typo</c>.</param>
/// <param name="WorktreePath">Where that branch is checked out.</param>
/// <param name="Changed">Whether any file differs from the base branch.</param>
/// <param name="DiffStat">The last line of <c>git diff --stat</c> against the base, or empty.</param>
/// <param name="Committed">Whether the change was committed on the branch.</param>
/// <param name="BuildOutcome">What this host's own build said afterwards, in one line.</param>
/// <param name="Summary">The coding agent's closing message, bounded.</param>
public sealed record CodeChange(
    string Branch,
    string WorktreePath,
    bool Changed,
    string DiffStat,
    bool Committed,
    string BuildOutcome,
    string Summary);
