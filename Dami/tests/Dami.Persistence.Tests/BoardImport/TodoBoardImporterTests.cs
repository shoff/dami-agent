using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Dami.Persistence.TaskBoard;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Dami.Persistence.Tests.BoardImport;

/// <summary>
/// Imports the repository's real TODO.md into real PostgreSQL. The parser tests fix the
/// grammar and the step tests fix the rerun rules; this one proves the two together survive
/// the store's guards, which live in SQL and cannot be exercised by a fake.
/// </summary>
[Collection(DatabaseCollection.NAME)]
public sealed class TodoBoardImporterTests
{
    private static readonly DateTimeOffset importedAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TaskActor importer = new("claude", TaskActorKind.Agent);

    private readonly DatabaseFixture fixture;
    private readonly ITestOutputHelper output;

    /// <summary>Creates the fixture.</summary>
    public TodoBoardImporterTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(output);
        this.fixture = fixture;
        this.output = output;
    }

    [Fact]
    public async Task ImportAsync_Should_Write_The_Whole_Blueprint_And_Then_Be_Idempotent()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var plan = RealPlan();

        var first = await this.ImportAsync(store, plan);
        var afterFirst = await store.FindAsync(plan.Draft.BoardId, CancellationToken.None);

        this.output.WriteLine($"board created: {first.BoardCreated}");
        this.output.WriteLine($"tasks:         {first.TasksWritten}");
        this.output.WriteLine($"mutations:     {first.MutationsApplied}");
        this.output.WriteLine($"conflicts:     {first.Conflicts.Count}");
        foreach (var conflict in first.Conflicts.Take(10))
        {
            this.output.WriteLine($"  {conflict}");
        }

        Assert.True(first.BoardCreated);
        Assert.NotNull(afterFirst);
        // TODO.md is a living file, so the expected shape comes from the plan, not a constant.
        Assert.Equal(plan.Draft.Tasks.Count, afterFirst.Tasks.Count);
        Assert.Equal(plan.Desired.Count, Flatten(afterFirst.Tasks).Count());
        Assert.Equal(plan.Desired.Count, first.TasksWritten);

        // A rerun must find the same board, not make a second one, and must not churn state.
        var second = await this.ImportAsync(store, RealPlan());
        this.output.WriteLine($"rerun mutations: {second.MutationsApplied}");

        Assert.False(second.BoardCreated);
        Assert.Equal(0, second.MutationsApplied);
    }

    [Fact]
    public async Task ImportAsync_Should_Reach_The_Statuses_The_File_States()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var plan = RealPlan();

        await this.ImportAsync(store, plan);
        var snapshot = await store.FindAsync(plan.Draft.BoardId, CancellationToken.None);
        var byId = Flatten(snapshot!.Tasks).ToDictionary(task => task.TaskId);

        // G2 is "[x] Context assembly"; it must be Done, and its epic G must exist above it.
        var g2 = byId[TaskId("G2")];
        Assert.Equal(TaskBoardStatus.Done, g2.Status);

        // E3 is "[ ] UDP path ... `[BLOCKED: L-phase]`" — open in the file, blocked in fact.
        Assert.Equal(TaskBoardStatus.Blocked, byId[TaskId("E3")].Status);

        // B7 is open with a trailing `[STEVE: whose memories are they]`, which is the same
        // "waiting on him" the leading marker means. B6 carries the leading form. Both land
        // as Blocked, because neither is work anyone can pick up.
        Assert.Equal(TaskBoardStatus.Blocked, byId[TaskId("B7")].Status);
        Assert.Equal(TaskBoardStatus.Blocked, byId[TaskId("B6")].Status);
    }

    [Fact]
    public async Task ImportAsync_Should_Nest_Sub_Tasks_And_Keep_Prerequisite_Edges()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var plan = RealPlan();

        await this.ImportAsync(store, plan);
        var snapshot = await store.FindAsync(plan.Draft.BoardId, CancellationToken.None);
        var byId = Flatten(snapshot!.Tasks).ToDictionary(task => task.TaskId);

        // G4c3a sits four levels below its epic and must still be the same task type.
        var deep = byId[TaskId("G4c3a")];
        Assert.StartsWith("G4c3a", deep.Title, StringComparison.Ordinal);

        // H9 "needs K1 first" is the one prose dependency in the file that names a real task.
        Assert.Contains(TaskId("K1"), byId[TaskId("H9")].PrerequisiteTaskIds);
    }

    [Fact]
    public async Task ImportAsync_Should_Leave_Work_The_Board_Finished_Since_The_File_Was_Written()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var plan = RealPlan();
        await this.ImportAsync(store, plan);

        // K1 is open in TODO.md. Somebody finishes it on the board without editing the file.
        var k1 = TaskId("K1");
        var before = await ReadAsync(store, plan.Draft.BoardId, k1);
        Assert.True(await store.TryClaimAsync(
            k1, before.Version, importer, importedAt, CancellationToken.None));
        var claimed = await ReadAsync(store, plan.Draft.BoardId, k1);
        Assert.True(await store.TryCompleteAsync(
            k1, claimed.Version, importer, importedAt, CancellationToken.None));

        var rerun = await this.ImportAsync(store, RealPlan());
        var after = await ReadAsync(store, plan.Draft.BoardId, k1);

        // The stale file still says open. The board's newer truth must survive the rerun.
        Assert.Equal(TaskBoardStatus.Done, after.Status);
        Assert.Equal(0, rerun.MutationsApplied);
    }

    [Fact]
    public async Task ImportAsync_Should_Record_Activity_Carrying_Actor_Time_And_Revision()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var plan = RealPlan();

        await this.ImportAsync(store, plan);

        var activity = new List<TaskBoardActivity>();
        await foreach (var item in store.ActivityAsync(plan.Draft.BoardId, 500, CancellationToken.None))
        {
            activity.Add(item);
        }

        Assert.NotEmpty(activity);
        Assert.All(activity, item => Assert.False(string.IsNullOrWhiteSpace(item.Actor.ActorId)));
        Assert.All(activity, item => Assert.NotEqual(default, item.OccurredAt));

        // The source revision has to be recoverable from the board itself, or an imported
        // board cannot be traced back to the file state that produced it.
        var snapshot = await store.FindAsync(plan.Draft.BoardId, CancellationToken.None);
        Assert.Contains("revision", snapshot!.Plan, StringComparison.Ordinal);
        Assert.Contains(activity, item => item.Detail?.Contains("TODO.md", StringComparison.Ordinal) == true);
    }

    private async Task<TodoImportReport> ImportAsync(PostgresTaskBoardStore store, TodoImportPlan plan)
    {
        var importerService = new TodoBoardImporter(
            store,
            TimeProvider.System,
            NullLogger<TodoBoardImporter>.Instance);
        return await importerService.ImportAsync(plan, importer, "test-revision", CancellationToken.None);
    }

    private static async Task<BoardTask> ReadAsync(PostgresTaskBoardStore store, Guid boardId, Guid taskId)
    {
        var snapshot = await store.FindAsync(boardId, CancellationToken.None);
        return Flatten(snapshot!.Tasks).Single(task => task.TaskId == taskId);
    }

    private static Guid TaskId(string todoId)
    {
        return BoardImportIds.Task(TodoBoardMapper.BOARD_KEY, todoId);
    }

    private static TodoImportPlan RealPlan()
    {
        var document = TodoBoardParser.Parse(File.ReadAllText(FindBoard()));
        return TodoBoardMapper.Map(
            document,
            new TodoImportSource("test-revision", "The Dami Core end state.", "Imported from TODO.md"),
            importer,
            importedAt);
    }

    private static IEnumerable<BoardTask> Flatten(IReadOnlyList<BoardTask> tasks)
    {
        foreach (var task in tasks)
        {
            yield return task;
            foreach (var child in Flatten(task.SubTasks))
            {
                yield return child;
            }
        }
    }

    private static string FindBoard()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "TODO.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException($"Could not locate TODO.md above {AppContext.BaseDirectory}.");
    }

    private PostgresTaskBoardStore CreateStore()
    {
        return new PostgresTaskBoardStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }
}
