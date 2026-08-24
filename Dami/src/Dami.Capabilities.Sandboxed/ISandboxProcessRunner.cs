namespace Dami.Capabilities.Sandboxed;

/// <summary>Executes one command in an externally bounded OS sandbox.</summary>
public interface ISandboxProcessRunner
{
    /// <summary>Runs the command with bounded input, output, time, memory, and processes.</summary>
    Task<SandboxProcessResult> RunAsync(
        string toolDirectory,
        SandboxMountAccess mountAccess,
        IReadOnlyList<string> command,
        string standardInput,
        CancellationToken cancellationToken);
}
