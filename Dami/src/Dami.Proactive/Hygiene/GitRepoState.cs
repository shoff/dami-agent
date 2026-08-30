using System.Diagnostics;
using System.Globalization;
using Dami.Contracts.Proactive;

namespace Dami.Proactive.Hygiene;

/// <summary>Reads a working copy by asking git, with read-only commands only.</summary>
/// <remarks>
/// Every invocation here is a query. There is deliberately no path in this class that can
/// stage, commit, fetch, or push: the service above it runs unattended every night, and a
/// nightly job that can write to a working copy will eventually write to it at the wrong
/// moment. Noticing is the whole job.
/// </remarks>
public sealed class GitRepoState : IRepoState
{
    /// <inheritdoc />
    public async Task<RepoState> ReadAsync(string repoPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        var inside = await this.RunAsync(repoPath, cancellationToken, "rev-parse", "--is-inside-work-tree")
            .ConfigureAwait(false);
        if (inside.Trim() != "true")
        {
            return RepoState.unknown;
        }

        var branch = (await this.RunAsync(repoPath, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD")
            .ConfigureAwait(false)).Trim();
        var upstream = (await this.RunAsync(
            repoPath, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}")
            .ConfigureAwait(false)).Trim();

        return new RepoState(
            true,
            branch,
            upstream.Length > 0,
            await this.CountAsync(repoPath, "@{u}..HEAD", cancellationToken).ConfigureAwait(false),
            await this.CountAsync(repoPath, "HEAD..@{u}", cancellationToken).ConfigureAwait(false),
            await this.OldestUnpushedAsync(repoPath, cancellationToken).ConfigureAwait(false),
            await this.DirtyAsync(repoPath, cancellationToken).ConfigureAwait(false),
            LastFetch(repoPath),
            await this.TrackedDdlAsync(repoPath, cancellationToken).ConfigureAwait(false));
    }

    private async Task<int> CountAsync(string repoPath, string range, CancellationToken cancellationToken)
    {
        var output = await this.RunAsync(repoPath, cancellationToken, "rev-list", "--count", range)
            .ConfigureAwait(false);
        return int.TryParse(output.Trim(), out var count) ? count : 0;
    }

    /// <remarks>
    /// Committer date, not author date: what matters is how long the commit has been
    /// sitting here, and a rebased or cherry-picked commit keeps an author date that can
    /// be much older than its time on this disk.
    /// </remarks>
    private async Task<DateTimeOffset?> OldestUnpushedAsync(
        string repoPath,
        CancellationToken cancellationToken)
    {
        var output = await this.RunAsync(
            repoPath, cancellationToken, "log", "@{u}..HEAD", "--reverse", "--format=%cI", "-1")
            .ConfigureAwait(false);
        return DateTimeOffset.TryParse(
            output.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : null;
    }

    /// <remarks>Porcelain v1, which is the format git promises not to change.</remarks>
    private async Task<IReadOnlyList<string>> DirtyAsync(
        string repoPath,
        CancellationToken cancellationToken)
    {
        var output = await this.RunAsync(repoPath, cancellationToken, "status", "--porcelain")
            .ConfigureAwait(false);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Length > 3 ? line[3..] : line)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> TrackedDdlAsync(
        string repoPath,
        CancellationToken cancellationToken)
    {
        var output = await this.RunAsync(repoPath, cancellationToken, "ls-files", "tools/ddl")
            .ConfigureAwait(false);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    /// <remarks>
    /// FETCH_HEAD's mtime, because git records no "last fetched" anywhere else. Absent
    /// when the remote has never been contacted, which is itself the answer.
    /// </remarks>
    private static DateTimeOffset? LastFetch(string repoPath)
    {
        var head = Path.Combine(repoPath, ".git", "FETCH_HEAD");
        return File.Exists(head) ? File.GetLastWriteTimeUtc(head) : null;
    }

    private async Task<string> RunAsync(
        string repoPath,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // A non-zero exit is an answer here, not a fault: `rev-parse @{u}` fails exactly
        // when there is no upstream, which is a thing worth reporting rather than throwing.
        return process.ExitCode == 0 ? output : string.Empty;
    }
}
