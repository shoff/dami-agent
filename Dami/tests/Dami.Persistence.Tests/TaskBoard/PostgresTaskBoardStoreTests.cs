using Dami.Contracts.TaskBoard;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Persistence.TaskBoard;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests.TaskBoard;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresTaskBoardStoreTests
{
    private static readonly DateTimeOffset createdAt =
        new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresTaskBoardStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task CreateAsync_Should_Round_Trip_One_Recursive_Task_Type()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var draft = CreateDraft(parentId, childId);

        await store.CreateAsync(draft, CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal((draft.BoardId, draft.Title, draft.FeatureRequest, draft.Plan),
            (found.BoardId, found.Title, found.FeatureRequest, found.Plan));
        var parent = Assert.Single(found.Tasks);
        Assert.Equal(parentId, parent.TaskId);
        Assert.Equal(TaskBoardStatus.Open, parent.Status);
        Assert.Null(parent.Claim);
        Assert.Equal("Round-trips a tree", Assert.Single(parent.AcceptanceCriteria).Description);
        Assert.Equal(childId, Assert.Single(parent.SubTasks).TaskId);
    }

    [Fact]
    public async Task FindAsync_Should_Respect_Ordered_And_Priority_Sibling_Modes()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var low = CreateLeaf("low", TaskPriority.Low, 0);
        var high = CreateLeaf("high", TaskPriority.High, 1);
        var parent = new BoardTaskDraft(
            Guid.NewGuid(), "parent", "priority children", TaskPriority.Normal, 1,
            TaskOrdering.Priority, [], [], [low, high]);
        var first = CreateLeaf("first", TaskPriority.Low, 0);
        var draft = CreateDraft([parent, first], TaskOrdering.Ordered);

        await store.CreateAsync(draft, CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.Equal(new[] { "first", "parent" }, found!.Tasks.Select(task => task.Title));
        Assert.Equal(new[] { "high", "low" },
            found.Tasks[1].SubTasks.Select(task => task.Title));
    }

    [Fact]
    public async Task TryClaimAsync_Should_Allow_Exactly_One_Concurrent_Actor()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var task = CreateLeaf("claim me", TaskPriority.Normal, 0);
        var draft = CreateDraft([task], TaskOrdering.Priority);
        await store.CreateAsync(draft, CancellationToken.None);

        var claims = await Task.WhenAll(
            store.TryClaimAsync(
                task.TaskId, 1, new TaskActor("codex", TaskActorKind.Agent),
                createdAt.AddMinutes(1), null, CancellationToken.None),
            store.TryClaimAsync(
                task.TaskId, 1, new TaskActor("steve", TaskActorKind.Human),
                createdAt.AddMinutes(1), null, CancellationToken.None));
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.Single(claims, result => result);
        Assert.Equal(TaskBoardStatus.InProgress, found!.Tasks[0].Status);
        Assert.Contains(found.Tasks[0].Claim!.Actor.ActorId, new[] { "codex", "steve" });
        Assert.Equal(2, found.Tasks[0].Version);
    }

    [Fact]
    public async Task TryClaimAsync_Should_Reject_An_Incomplete_Prerequisite()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var prerequisite = CreateLeaf("first", TaskPriority.High, 0);
        var dependent = new BoardTaskDraft(
            Guid.NewGuid(), "second", "depends on first", TaskPriority.High, 1,
            TaskOrdering.Ordered, [prerequisite.TaskId], [], []);
        var draft = CreateDraft([prerequisite, dependent], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);

        var claimed = await store.TryClaimAsync(
            dependent.TaskId, 1, new TaskActor("codex", TaskActorKind.Agent),
            createdAt.AddMinutes(1), null, CancellationToken.None);

        Assert.False(claimed);
    }

    [Fact]
    public async Task TrySetCriterionAsync_Should_Record_Evidence_And_Advance_Task_Version()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var criterion = new AcceptanceCriterionDraft(Guid.NewGuid(), "Observed live", 0);
        var task = new BoardTaskDraft(
            Guid.NewGuid(), "prove it", "needs evidence", TaskPriority.Normal, 0,
            TaskOrdering.Ordered, [], [criterion], []);
        var draft = CreateDraft([task], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);
        var actor = new TaskActor("steve", TaskActorKind.Human);

        var changed = await store.TrySetCriterionAsync(
            criterion.CriterionId, 1, true, actor, createdAt.AddMinutes(1),
            CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.True(changed);
        var persistedTask = Assert.Single(found!.Tasks);
        var persistedCriterion = Assert.Single(persistedTask.AcceptanceCriteria);
        Assert.True(persistedCriterion.IsSatisfied);
        Assert.Equal(actor, persistedCriterion.SatisfiedBy);
        Assert.Equal(createdAt.AddMinutes(1), persistedCriterion.SatisfiedAt);
        Assert.Equal(2, persistedTask.Version);
    }

    [Fact]
    public async Task TryCompleteAsync_Should_Require_Acceptance_And_Completed_SubTasks()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var actor = new TaskActor("codex", TaskActorKind.Agent);
        var criterion = new AcceptanceCriterionDraft(Guid.NewGuid(), "verified", 0);
        var child = CreateLeaf("child", TaskPriority.High, 0);
        var parent = new BoardTaskDraft(
            Guid.NewGuid(), "parent", "owns child", TaskPriority.High, 0,
            TaskOrdering.Ordered, [], [criterion], [child]);
        var draft = CreateDraft([parent], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);
        await store.TryClaimAsync(
            parent.TaskId, 1, actor, createdAt.AddMinutes(1), null, CancellationToken.None);
        await store.TrySetCriterionAsync(
            criterion.CriterionId, 2, true, actor, createdAt.AddMinutes(2),
            CancellationToken.None);

        var premature = await store.TryCompleteAsync(
            parent.TaskId, 3, actor, createdAt.AddMinutes(3), null, CancellationToken.None);
        await store.TryClaimAsync(
            child.TaskId, 1, actor, createdAt.AddMinutes(4), null, CancellationToken.None);
        var childDone = await store.TryCompleteAsync(
            child.TaskId, 2, actor, createdAt.AddMinutes(5), null, CancellationToken.None);
        var parentDone = await store.TryCompleteAsync(
            parent.TaskId, 3, actor, createdAt.AddMinutes(6), null, CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.Equal((false, true, true), (premature, childDone, parentDone));
        Assert.Equal(TaskBoardStatus.Done, found!.Tasks[0].Status);
        Assert.Equal(TaskBoardStatus.Done, found.Tasks[0].SubTasks[0].Status);
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_A_Prerequisite_Cycle_Without_Partial_Data()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new BoardTaskDraft(
            firstId, "first", "cycle", TaskPriority.Normal, 0,
            TaskOrdering.Ordered, [secondId], [], []);
        var second = new BoardTaskDraft(
            secondId, "second", "cycle", TaskPriority.Normal, 1,
            TaskOrdering.Ordered, [firstId], [], []);
        var draft = CreateDraft([first, second], TaskOrdering.Ordered);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync(draft, CancellationToken.None));

        Assert.Null(await store.FindAsync(draft.BoardId, CancellationToken.None));
    }

    [Fact]
    public async Task Runtime_Role_Should_Create_Read_And_Claim_Without_Ddl_Privilege()
    {
        await this.fixture.ResetAsync();
        await using var runtimeSource = DatabaseFixture.CreateRuntimeDataSource();
        var store = new PostgresTaskBoardStore(
            runtimeSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        var task = CreateLeaf("runtime", TaskPriority.Normal, 0);
        var draft = CreateDraft([task], TaskOrdering.Ordered);

        await store.CreateAsync(draft, CancellationToken.None);
        var claimed = await store.TryClaimAsync(
            task.TaskId, 1, new TaskActor("dami", TaskActorKind.Agent),
            createdAt.AddMinutes(1), null, CancellationToken.None);

        Assert.True(claimed);
        Assert.NotNull(await store.FindAsync(draft.BoardId, CancellationToken.None));
    }

    [Fact]
    public async Task Runtime_Role_Should_Reject_An_Unaudited_Direct_Task_Update()
    {
        await this.fixture.ResetAsync();
        await using var runtimeSource = DatabaseFixture.CreateRuntimeDataSource();
        var store = new PostgresTaskBoardStore(
            runtimeSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        var task = CreateLeaf("guarded", TaskPriority.Normal, 0);
        await store.CreateAsync(CreateDraft([task], TaskOrdering.Ordered), CancellationToken.None);
        await using var command = runtimeSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.task_board_tasks set status = 'Blocked' "
            + "where task_id = @task;");
        command.Parameters.AddWithValue("task", task.TaskId);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task ActivityAsync_Should_Record_Each_Mutation_In_Durable_Order()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var criterion = new AcceptanceCriterionDraft(Guid.NewGuid(), "proven", 0);
        var task = new BoardTaskDraft(
            Guid.NewGuid(), "tracked", "audit every change", TaskPriority.High, 0,
            TaskOrdering.Ordered, [], [criterion], []);
        var draft = CreateDraft([task], TaskOrdering.Ordered);
        var agent = new TaskActor("codex", TaskActorKind.Agent);
        await store.CreateAsync(draft, CancellationToken.None);
        await store.TryClaimAsync(
            task.TaskId, 1, agent, createdAt.AddMinutes(1), null, CancellationToken.None);
        await store.TrySetCriterionAsync(
            criterion.CriterionId, 2, true, agent, createdAt.AddMinutes(2),
            CancellationToken.None);
        await store.TryCompleteAsync(
            task.TaskId, 3, agent, createdAt.AddMinutes(3), null, CancellationToken.None);

        var activity = new List<TaskBoardActivity>();
        await foreach (var item in store.ActivityAsync(
            draft.BoardId, 20, CancellationToken.None))
        {
            activity.Add(item);
        }

        Assert.Equal(
            new[] { TaskBoardActivityKind.BoardCreated, TaskBoardActivityKind.TaskClaimed,
                TaskBoardActivityKind.CriterionSatisfied, TaskBoardActivityKind.TaskCompleted },
            activity.Select(item => item.Kind));
        Assert.Equal(new[] { "steve", "codex", "codex", "codex" },
            activity.Select(item => item.Actor.ActorId));
    }

    [Fact]
    public async Task Claim_And_Completion_Should_Carry_Detail_When_Given_And_Omit_It_When_Blank()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var task = CreateLeaf("annotated", TaskPriority.Normal, 0);
        var draft = CreateDraft([task], TaskOrdering.Ordered);
        var agent = new TaskActor("claude", TaskActorKind.Agent);
        await store.CreateAsync(draft, CancellationToken.None);
        await store.TryClaimAsync(
            task.TaskId, 1, agent, createdAt.AddMinutes(1), "[imported at abc1234]",
            CancellationToken.None);
        await store.TryCompleteAsync(
            task.TaskId, 2, agent, createdAt.AddMinutes(2), "   ", CancellationToken.None);

        var activity = new List<TaskBoardActivity>();
        await foreach (var item in store.ActivityAsync(
            draft.BoardId, 20, CancellationToken.None))
        {
            activity.Add(item);
        }

        var claimed = Assert.Single(activity, item => item.Kind == TaskBoardActivityKind.TaskClaimed);
        var completed = Assert.Single(activity, item => item.Kind == TaskBoardActivityKind.TaskCompleted);
        Assert.Equal("[imported at abc1234]", claimed.Detail);
        Assert.Null(completed.Detail);
    }

    [Fact]
    public async Task Activity_Should_Reject_Update_Tampering()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var draft = CreateDraft(
            [CreateLeaf("immutable", TaskPriority.Normal, 0)], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);
        await using var update = this.fixture.DataSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.task_board_activity set actor_id = 'tampered';");

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => update.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal("23001", error.SqlState);
    }

    [Fact]
    public async Task TrySetStatusAsync_Should_Block_With_A_Reason_And_Reopen_Unclaimed()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var task = CreateLeaf("status", TaskPriority.Normal, 0);
        var draft = CreateDraft([task], TaskOrdering.Ordered);
        var actor = new TaskActor("codex", TaskActorKind.Agent);
        await store.CreateAsync(draft, CancellationToken.None);
        await store.TryClaimAsync(
            task.TaskId, 1, actor, createdAt.AddMinutes(1), null, CancellationToken.None);

        var blocked = await store.TrySetStatusAsync(
            task.TaskId, 2, TaskBoardStatus.Blocked, actor, "waiting for access",
            createdAt.AddMinutes(2), CancellationToken.None);
        var reopened = await store.TrySetStatusAsync(
            task.TaskId, 3, TaskBoardStatus.Open, actor, "access granted",
            createdAt.AddMinutes(3), CancellationToken.None);
        var bypassed = await store.TrySetStatusAsync(
            task.TaskId, 4, TaskBoardStatus.Done, actor, "skip evidence",
            createdAt.AddMinutes(4), CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);
        var activity = await ReadActivityAsync(store, draft.BoardId);

        Assert.Equal((true, true, false), (blocked, reopened, bypassed));
        Assert.Equal(TaskBoardStatus.Open, found!.Tasks[0].Status);
        Assert.Null(found.Tasks[0].Claim);
        Assert.Equal((TaskBoardStatus.Blocked, TaskBoardStatus.Open, "access granted"),
            (activity[^1].FromStatus, activity[^1].ToStatus, activity[^1].Detail));
    }

    [Fact]
    public async Task ListRecentAsync_Should_Derive_Counts_Status_And_Latest_Activity()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var activeTask = CreateLeaf("active", TaskPriority.High, 0);
        var active = CreateDraft([activeTask], TaskOrdering.Ordered);
        var newer = new TaskBoardDraft(
            Guid.NewGuid(), "newer", "request", "plan",
            new TaskActor("steve", TaskActorKind.Human), createdAt.AddMinutes(1),
            TaskOrdering.Ordered, [CreateLeaf("open", TaskPriority.Normal, 0)]);
        await store.CreateAsync(active, CancellationToken.None);
        await store.CreateAsync(newer, CancellationToken.None);
        await store.TryClaimAsync(
            activeTask.TaskId, 1, new TaskActor("codex", TaskActorKind.Agent),
            createdAt.AddMinutes(2), null, CancellationToken.None);

        var summaries = new List<TaskBoardSummary>();
        await foreach (var summary in store.ListRecentAsync(1, CancellationToken.None))
        {
            summaries.Add(summary);
        }

        var found = Assert.Single(summaries);
        Assert.Equal((active.BoardId, TaskBoardStatus.InProgress, 1, 0, 0),
            (found.BoardId, found.Status, found.TotalTasks, found.DoneTasks, found.BlockedTasks));
        Assert.Equal(createdAt.AddMinutes(2), found.UpdatedAt);
    }

    [Fact]
    public async Task FindAsync_Should_Derive_Board_Status_From_Its_Tasks()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var task = CreateLeaf("active", TaskPriority.High, 0);
        var draft = CreateDraft([task], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);
        await store.TryClaimAsync(
            task.TaskId, 1, new TaskActor("codex", TaskActorKind.Agent),
            createdAt.AddMinutes(1), null, CancellationToken.None);

        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.Equal(TaskBoardStatus.InProgress, found!.Status);
    }

    [Fact]
    public async Task CreateAsync_Should_Converge_An_Exact_Retry_And_Reject_A_Conflict()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var draft = CreateDraft(
            [CreateLeaf("idempotent", TaskPriority.Normal, 0)], TaskOrdering.Ordered);
        await store.CreateAsync(draft, CancellationToken.None);

        var retryError = await Record.ExceptionAsync(
            () => store.CreateAsync(draft, CancellationToken.None));
        var conflicting = new TaskBoardDraft(
            draft.BoardId, "different", draft.FeatureRequest, draft.Plan,
            draft.CreatedBy, draft.CreatedAt, draft.RootOrdering, draft.Tasks);

        Assert.Null(retryError);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(conflicting, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Round_Trip_Planning_Provenance()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var context = new TaskBoardPlanningContext(
            FeaturePlannerKind.Frontier, PrivacyClass.Egressable, ExecutionOrigin.UserTurn);
        var original = CreateDraft(
            [CreateLeaf("provenance", TaskPriority.Normal, 0)], TaskOrdering.Ordered);
        var draft = new TaskBoardDraft(
            original.BoardId, original.Title, original.FeatureRequest, original.Plan,
            original.CreatedBy, original.CreatedAt, original.RootOrdering, original.Tasks,
            context);

        await store.CreateAsync(draft, CancellationToken.None);
        var found = await store.FindAsync(draft.BoardId, CancellationToken.None);

        Assert.Equal(context, found!.PlanningContext);
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_More_Than_1024_Tasks()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var tasks = Enumerable.Range(0, 1025)
            .Select(index => CreateLeaf($"task-{index}", TaskPriority.Normal, index))
            .ToArray();
        var draft = CreateDraft(tasks, TaskOrdering.Ordered);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync(draft, CancellationToken.None));

        Assert.Null(await store.FindAsync(draft.BoardId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_More_Than_64_Task_Levels()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var nested = CreateLeaf("level-65", TaskPriority.Normal, 0);
        for (var level = 64; level >= 1; level--)
        {
            nested = new BoardTaskDraft(
                Guid.NewGuid(), $"level-{level}", string.Empty, TaskPriority.Normal, 0,
                TaskOrdering.Ordered, [], [], [nested]);
        }

        var draft = CreateDraft([nested], TaskOrdering.Ordered);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync(draft, CancellationToken.None));

        Assert.Null(await store.FindAsync(draft.BoardId, CancellationToken.None));
    }

    private static TaskBoardDraft CreateDraft(Guid parentId, Guid childId)
    {
        var child = new BoardTaskDraft(
            childId,
            "Schema",
            "Create relational tables.",
            TaskPriority.High,
            0,
            TaskOrdering.Ordered,
            [],
            [],
            []);
        var parent = new BoardTaskDraft(
            parentId,
            "Persistence",
            "Create the recursive store.",
            TaskPriority.Critical,
            0,
            TaskOrdering.Priority,
            [],
            [new AcceptanceCriterionDraft(Guid.NewGuid(), "Round-trips a tree", 0)],
            [child]);
        return new TaskBoardDraft(
            Guid.NewGuid(),
            "PostgreSQL task board",
            "Let humans and agents plan work together.",
            "Persist the plan, enforce workflow, then render it twice.",
            new TaskActor("steve", TaskActorKind.Human),
            createdAt,
            TaskOrdering.Ordered,
            [parent]);
    }

    private static TaskBoardDraft CreateDraft(
        IReadOnlyList<BoardTaskDraft> tasks,
        TaskOrdering ordering)
    {
        return new TaskBoardDraft(
            Guid.NewGuid(), "board", "feature request", "implementation plan",
            new TaskActor("steve", TaskActorKind.Human), createdAt, ordering, tasks);
    }

    private static BoardTaskDraft CreateLeaf(
        string title,
        TaskPriority priority,
        int position)
    {
        return new BoardTaskDraft(
            Guid.NewGuid(), title, title, priority, position, TaskOrdering.Ordered,
            [], [], []);
    }

    private PostgresTaskBoardStore CreateStore()
    {
        return new PostgresTaskBoardStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private static async Task<IReadOnlyList<TaskBoardActivity>> ReadActivityAsync(
        PostgresTaskBoardStore store,
        Guid boardId)
    {
        var activity = new List<TaskBoardActivity>();
        await foreach (var item in store.ActivityAsync(boardId, 20, CancellationToken.None))
        {
            activity.Add(item);
        }

        return activity;
    }
}
