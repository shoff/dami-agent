using System.Diagnostics;

namespace Dami.Providers;

/// <summary>Runs the real codex CLI.</summary>
public sealed class CodexProcess : ICodexProcess
{
    /// <inheritdoc />
    public async Task<string> RunAsync(
        string binaryPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binaryPath);
        ArgumentNullException.ThrowIfNull(arguments);

        var lastMessagePath = Path.Combine(
            Path.GetTempPath(), $"dami-codex-{Guid.NewGuid():N}.txt");

        using var process = Start(binaryPath, arguments, lastMessagePath);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await AwaitResultAsync(process, lastMessagePath, timeoutSource.Token, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"codex exceeded {timeout.TotalSeconds:0}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            TryDelete(lastMessagePath);
        }
    }

    private static Process Start(
        string binaryPath,
        IReadOnlyList<string> arguments,
        string lastMessagePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--output-last-message");
        startInfo.ArgumentList.Add(lastMessagePath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {binaryPath}");
    }

    private static async Task<string> AwaitResultAsync(
        Process process,
        string lastMessagePath,
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        // Drain both pipes so the process cannot block on a full buffer.
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutToken);
        await process.StandardOutput.ReadToEndAsync(timeoutToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(timeoutToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"codex exited {process.ExitCode}: {Truncate(stderr)}");
        }

        return File.Exists(lastMessagePath)
            ? (await File.ReadAllTextAsync(lastMessagePath, cancellationToken).ConfigureAwait(false)).Trim()
            : throw new InvalidOperationException("codex produced no last-message file");
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leaked temp file is not worth failing a completed call over.
        }
    }

    private static string Truncate(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }
}
