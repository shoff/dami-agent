using System.Diagnostics;

namespace Dami.Proactive.CodeAudit;

/// <summary>Runs the real git, read-only flags only.</summary>
public sealed class GitProcessLog : IGitLog
{
    /// <inheritdoc />
    public async Task<string> RecentPatchAsync(
        string repoPath,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add("log");
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add($"--since={window.TotalHours:F0}.hours");
        process.StartInfo.ArgumentList.Add("--no-color");
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? output : string.Empty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DateTimeOffset>> CommitTimesAsync(
        string repoPath, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add("log");
        process.StartInfo.ArgumentList.Add("--no-merges");
        process.StartInfo.ArgumentList.Add("--format=%cI");
        process.StartInfo.ArgumentList.Add($"--since={since:O}");
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? ParseTimes(output) : [];
    }

    private static List<DateTimeOffset> ParseTimes(string output)
    {
        var times = new List<DateTimeOffset>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DateTimeOffset.TryParse(line, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time))
            {
                times.Add(time);
            }
        }

        return times;
    }
}
