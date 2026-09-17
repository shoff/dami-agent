namespace Dami.Contracts.Code;

/// <summary>Changes, explains, and lists work on the code this host owns (ADR-0036).</summary>
/// <remarks>
/// The fourth door through the boundary, and the first that writes to disk: a task
/// described in words goes to a coding agent on the subscription, and a branch comes
/// back. Implementations enforce rather than trust — the main working tree is never
/// touched, every change lands on its own branch in its own worktree, nothing is merged,
/// pushed, or deployed, and every run lands in the event stream with its purpose and
/// never the task text. Steve reviews and merges; the tool's authority ends at the branch.
/// </remarks>
public interface ICodeWorker
{
    /// <summary>Whether code work is switched on at all. Off means the tools are not offered.</summary>
    bool Enabled { get; }

    /// <summary>Carries out a task on a fresh branch and reports what changed.</summary>
    /// <exception cref="Privacy.EgressRefusedException">Code work is off, or the budget is spent.</exception>
    Task<CodeChange> ChangeAsync(CodeChangeRequest request, CancellationToken cancellationToken);

    /// <summary>Answers a question about the code without changing anything.</summary>
    /// <exception cref="Privacy.EgressRefusedException">Code work is off, or the budget is spent.</exception>
    Task<string> ExplainAsync(CodeQuestion question, CancellationToken cancellationToken);

    /// <summary>The branches earlier changes produced, newest first.</summary>
    Task<IReadOnlyList<CodeBranch>> ListChangesAsync(CancellationToken cancellationToken);
}
