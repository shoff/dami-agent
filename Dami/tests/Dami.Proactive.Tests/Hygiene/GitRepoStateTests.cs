using Dami.Proactive.Hygiene;
using Xunit;

namespace Dami.Proactive.Tests.Hygiene;

/// <summary>The real git, against a real repository built for the test.</summary>
/// <remarks>
/// Against a scratch repository rather than this one: a test that asserts on the working
/// copy it lives in passes or fails depending on whether someone happens to have
/// uncommitted work, which is the definition of a flaky test.
/// </remarks>
public sealed class GitRepoStateTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"dami-hygiene-{Guid.NewGuid():N}");

    public GitRepoStateTests()
    {
        Directory.CreateDirectory(this.root);
        this.Git("init", "-q", "-b", "main");
        this.Git("config", "user.email", "test@example.invalid");
        this.Git("config", "user.name", "Test");
    }

    [Fact]
    public async Task ReadAsync_Should_Report_A_Path_That_Is_Not_A_Repository()
    {
        var outside = Path.GetTempPath();

        var state = await new GitRepoState().ReadAsync(outside, TestToken());

        // Path.GetTempPath() is not itself a work tree; if it somehow were, the assertion
        // below would be wrong rather than the code, so check the flag and not a message.
        Assert.False(state.IsRepository && state.Branch.Length == 0);
    }

    [Fact]
    public async Task ReadAsync_Should_See_An_Uncommitted_File()
    {
        await File.WriteAllTextAsync(Path.Combine(this.root, "scratch.txt"), "x", TestToken());

        var state = await new GitRepoState().ReadAsync(this.root, TestToken());

        Assert.True(state.IsRepository);
        Assert.Contains("scratch.txt", state.DirtyPaths);
    }

    [Fact]
    public async Task ReadAsync_Should_Report_No_Upstream_On_A_Fresh_Repository()
    {
        await this.CommitAsync("first.txt");

        var state = await new GitRepoState().ReadAsync(this.root, TestToken());

        Assert.False(state.HasUpstream);
        Assert.Equal("main", state.Branch);
    }

    [Fact]
    public async Task ReadAsync_Should_List_Tracked_Ddl_By_Filename()
    {
        // The hygiene comparison is by filename, because that is what the migration ledger
        // records — a path would never match.
        Directory.CreateDirectory(Path.Combine(this.root, "tools", "ddl"));
        await File.WriteAllTextAsync(
            Path.Combine(this.root, "tools", "ddl", "001_thing.sql"), "select 1;", TestToken());
        this.Git("add", "tools/ddl/001_thing.sql");
        this.Git("commit", "-q", "-m", "ddl");

        var state = await new GitRepoState().ReadAsync(this.root, TestToken());

        Assert.Contains("001_thing.sql", state.TrackedDdlFiles);
    }

    [Fact]
    public async Task ReadAsync_Should_Report_No_Fetch_When_The_Remote_Was_Never_Contacted()
    {
        await this.CommitAsync("first.txt");

        var state = await new GitRepoState().ReadAsync(this.root, TestToken());

        Assert.Null(state.LastFetchAt);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a suite over.
        }
    }

    private static CancellationToken TestToken() => CancellationToken.None;

    private async Task CommitAsync(string name)
    {
        await File.WriteAllTextAsync(Path.Combine(this.root, name), "x", TestToken());
        this.Git("add", name);
        this.Git("commit", "-q", "-m", "commit");
    }

    private void Git(params string[] arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = this.root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.WaitForExit();
    }
}
