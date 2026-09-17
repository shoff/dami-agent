using System.Diagnostics;

namespace Dami.Providers;

/// <summary>Runs a real process with both pipes drained and a hard ceiling.</summary>
public sealed class CommandRunner : ICommandRunner
{
    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(
        string workingDirectory,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = Start(workingDirectory, executable, arguments);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await AwaitResultAsync(process, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"{executable} {arguments.FirstOrDefault()} exceeded {timeout.TotalSeconds:0}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static Process Start(string workingDirectory, string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {executable}");
    }

    private static async Task<CommandResult> AwaitResultAsync(Process process, CancellationToken token)
    {
        // Drain both pipes so the process cannot block on a full buffer.
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        var stdout = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);

        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
        return new CommandResult(process.ExitCode, output.Trim());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone; nothing to kill.
        }
    }
}
