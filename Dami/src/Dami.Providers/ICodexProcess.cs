namespace Dami.Providers;

/// <summary>The seam between the adapter's policy and the operating system.</summary>
/// <remarks>Exists so the gate's tests never spawn a real process.</remarks>
public interface ICodexProcess
{
    /// <summary>Runs the codex binary and returns the last-message file's content.</summary>
    Task<string> RunAsync(
        string binaryPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
