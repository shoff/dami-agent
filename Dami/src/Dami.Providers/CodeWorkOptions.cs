namespace Dami.Providers;

/// <summary>Where and how the coding agent works on this host's own code (ADR-0036).</summary>
/// <remarks>
/// Off by default: giving the frontier a door that writes to disk is a deliberate,
/// visible act, here and in the drop-in (<c>CodeWork__Enabled=true</c>). The repository is
/// only ever the base; every task gets its own worktree and branch under
/// <see cref="WorktreeRoot"/>, and nothing here merges, pushes, or deploys.
/// </remarks>
public sealed class CodeWorkOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "CodeWork";

    /// <summary>Whether the code tools are offered at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>The repository to branch from. Never written to directly.</summary>
    public string Repository { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>Where each task's worktree is created, one directory per branch.</summary>
    public string WorktreeRoot { get; set; } = "/home/steve/.local/share/dami/code";

    /// <summary>The branch every task starts from.</summary>
    public string BaseBranch { get; set; } = "main";

    /// <summary>The prefix every task branch carries; also what <c>list_code_changes</c> lists.</summary>
    public string BranchPrefix { get; set; } = "dami/";

    /// <summary>The solution this host builds after the agent is done, relative to the worktree.</summary>
    public string Solution { get; set; } = "Dami/Dami.sln";

    /// <summary>
    /// Wall-clock ceiling for the coding agent. It sits inside the frontier turn's own
    /// 600 s deadline (<see cref="CodexOptions.TimeoutSeconds"/>) together with the build,
    /// so a task that runs long becomes a tool result the turn can explain.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 360;

    /// <summary>Wall-clock ceiling for this host's own build of the result.</summary>
    public int BuildTimeoutSeconds { get; set; } = 180;

    /// <summary>How much of the agent's closing message is passed back to the frontier.</summary>
    public int MaxSummaryChars { get; set; } = 1500;
}
