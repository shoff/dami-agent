namespace Dami.Proactive.CodeAudit;

/// <summary>Where and how much the codebase audit reads (D-016: read-only).</summary>
public sealed class CodebaseAuditOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "CodebaseAudit";

    /// <summary>The repository to audit.</summary>
    public string RepoPath { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>How far back one pass looks.</summary>
    public int WindowHours { get; set; } = 168;

    /// <summary>Patch text beyond this is truncated before review.</summary>
    public int MaxPatchChars { get; set; } = 16000;
}
