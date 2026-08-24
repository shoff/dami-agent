namespace Dami.Capabilities.Sandboxed;

/// <summary>Bounded observable output from one completed sandbox process.</summary>
public sealed record SandboxProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
