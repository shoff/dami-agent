namespace Dami.Providers;

/// <summary>The subscription-frontier adapter's configuration (ADR-0011).</summary>
public sealed class CodexOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Codex";

    /// <summary>
    /// Whether the subscription frontier is enabled.
    /// </summary>
    /// <remarks>
    /// Replaces ADR-0010's host allowlist for the subprocess path, where transport-level
    /// allowlisting is not enforceable. False by default: frontier capability is a
    /// deliberate visible act, here and in the composition root.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>The codex binary. Steve's login lives with it; the adapter never touches credentials.</summary>
    public string BinaryPath { get; set; } = "/home/steve/.local/bin/codex";

    /// <summary>Optional model override (-m). Empty uses the CLI's configured default.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Scratch working directory for the sandboxed process.</summary>
    public string WorkingDirectory { get; set; } = "/tmp";

    /// <summary>Wall-clock ceiling per completion.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Wall-clock ceiling for a nested subscription image call. Shorter than the outer
    /// completion deadline so a failed picture becomes a tool result the turn can explain.
    /// </summary>
    public int ImageTimeoutSeconds { get; set; } = 480;

    /// <summary>
    /// How long a turn may stay silent before its first token or tool call. On
    /// 2026-09-03 a turn produced nothing for the full 600 s; this ends that in about
    /// a minute and resets the app-server. Reasoning that takes longer than this before
    /// saying anything is a hang for a chat turn, whatever it is for a coding one.
    /// </summary>
    public int FirstTokenTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// The app-server's sandbox for chat threads. Read-only: a chat turn has no business
    /// writing files, and the "workspace-write" the image generator uses is a separate
    /// <c>codex exec</c> with its own working directory.
    /// </summary>
    public string Sandbox { get; set; } = "read-only";

    /// <summary>
    /// Whether Codex may use its own built-in web search on chat threads. Off: on
    /// 2026-09-05 a turn "verified postings at the source" through it, bypassing the
    /// gated research door entirely (ADR-0033). Research goes through search_web.
    /// </summary>
    public bool BuiltInWebSearch { get; set; }
}
