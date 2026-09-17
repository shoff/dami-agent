namespace Dami.Providers;

/// <summary>Runs one local command to completion and returns what it said.</summary>
/// <remarks>
/// A seam, so the code worker's tests never spawn git or dotnet. No shell is involved:
/// the executable and its arguments are passed as a list, never a string.
/// </remarks>
public interface ICommandRunner
{
    /// <summary>Runs the command in the directory and waits for it, within the timeout.</summary>
    /// <exception cref="TimeoutException">The command outlived its ceiling and was killed.</exception>
    Task<CommandResult> RunAsync(
        string workingDirectory,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
