namespace Dami.Contracts.Code;

/// <summary>One branch an earlier code task produced.</summary>
/// <param name="Name">The branch name.</param>
/// <param name="CommittedAt">When its last commit was made.</param>
/// <param name="Subject">Its last commit's subject line.</param>
/// <param name="DiffStat">The last line of <c>git diff --stat</c> against the base branch.</param>
public sealed record CodeBranch(string Name, DateTimeOffset CommittedAt, string Subject, string DiffStat);
