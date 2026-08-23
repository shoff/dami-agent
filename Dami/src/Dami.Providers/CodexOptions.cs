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
}
