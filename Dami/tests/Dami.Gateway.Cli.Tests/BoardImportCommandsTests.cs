using Dami.Contracts.TaskBoard;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class BoardImportCommandsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private const string SMALL_BOARD = """
        # Board

        ## A · First

        - [x] A1 Done thing
        - [ ] A2 Open thing `[BLOCKED: waiting]`
          - [~ Claude 2026-08-25] A2a Claimed child
        """;

    [Fact]
    public async Task ImportAsync_Should_Refuse_A_Missing_File_Without_Touching_The_Store()
    {
        var store = new RefusingStore();
        var commands = Create(store);

        var (exitCode, _) = await CaptureAsync(() => commands.ImportAsync(
            ["/nonexistent/TODO.md", "--revision", "abc1234", "--actor", "claude"],
            CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task ImportAsync_Should_Require_A_Revision_And_An_Actor()
    {
        var path = await WriteBoardAsync();
        var store = new RefusingStore();
        var commands = Create(store);

        var (noRevision, _) = await CaptureAsync(() => commands.ImportAsync(
            [path, "--actor", "claude"], CancellationToken.None));
        var (noActor, _) = await CaptureAsync(() => commands.ImportAsync(
            [path, "--revision", "abc1234"], CancellationToken.None));

        Assert.Equal(2, noRevision);
        Assert.Equal(2, noActor);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task ImportAsync_Should_Report_The_Plan_On_A_Dry_Run_And_Write_Nothing()
    {
        var path = await WriteBoardAsync();
        var store = new RefusingStore();
        var commands = Create(store);

        var (exitCode, output) = await CaptureAsync(() => commands.ImportAsync(
            [path, "--revision", "abc1234", "--actor", "claude", "--agent", "--dry-run"],
            CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, store.Calls);
        // One epic root plus three entries.
        Assert.Contains("tasks:       4", output, StringComparison.Ordinal);
        Assert.Contains("dry run", output, StringComparison.Ordinal);
        Assert.Contains("abc1234", output, StringComparison.Ordinal);
    }

    private static BoardImportCommands Create(ITaskBoardStore store)
    {
        return new BoardImportCommands(
            store, new FakeTimeProvider(at), NullLoggerFactory.Instance);
    }

    private static async Task<string> WriteBoardAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dami-board-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path, SMALL_BOARD);
        return path;
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var original = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            var exitCode = await run();
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }
    }

    /// <summary>A store that counts every call and refuses it. A dry run must never reach it.</summary>
    private sealed class RefusingStore : ITaskBoardStore
    {
        public int Calls { get; private set; }

        public Task CreateAsync(TaskBoardDraft draft, CancellationToken cancellationToken)
        {
            return this.RefuseAsync<bool>();
        }

        public Task<TaskBoardSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken)
        {
            return this.RefuseAsync<TaskBoardSnapshot?>();
        }

        public IAsyncEnumerable<TaskBoardSummary> ListRecentAsync(
            int limit, CancellationToken cancellationToken)
        {
            this.Calls++;
            throw new InvalidOperationException("The store must not be touched.");
        }

        public Task<bool> TryClaimAsync(
            Guid taskId, long expectedVersion, TaskActor actor, DateTimeOffset claimedAt,
            CancellationToken cancellationToken)
        {
            return this.RefuseAsync<bool>();
        }

        public Task<bool> TrySetCriterionAsync(
            Guid criterionId, long expectedTaskVersion, bool isSatisfied, TaskActor actor,
            DateTimeOffset changedAt, CancellationToken cancellationToken)
        {
            return this.RefuseAsync<bool>();
        }

        public Task<bool> TryCompleteAsync(
            Guid taskId, long expectedVersion, TaskActor actor, DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            return this.RefuseAsync<bool>();
        }

        public Task<bool> TrySetStatusAsync(
            Guid taskId, long expectedVersion, TaskBoardStatus status, TaskActor actor,
            string detail, DateTimeOffset changedAt, CancellationToken cancellationToken)
        {
            return this.RefuseAsync<bool>();
        }

        public IAsyncEnumerable<TaskBoardActivity> ActivityAsync(
            Guid boardId, int limit, CancellationToken cancellationToken)
        {
            this.Calls++;
            throw new InvalidOperationException("The store must not be touched.");
        }

        private Task<T> RefuseAsync<T>()
        {
            this.Calls++;
            throw new InvalidOperationException("The store must not be touched.");
        }
    }
}
