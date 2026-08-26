using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.TaskBoard;
using Dami.Core.TaskBoard;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class TaskBoardEndpointsTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_Should_Return_Recent_Board_Summaries()
    {
        var summary = new TaskBoardSummary(
            Guid.NewGuid(), "Task board", TaskBoardStatus.InProgress,
            new DateTimeOffset(2026, 8, 24, 22, 30, 0, TimeSpan.Zero), 4, 1, 0);
        var store = new StubStore(summary);
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/task-boards?limit=10", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var item = Assert.Single(body!.RootElement.EnumerateArray());
        Assert.Equal(summary.BoardId, item.GetProperty("boardId").GetGuid());
        Assert.Equal(10, store.LastLimit);
    }

    [Fact]
    public async Task List_Should_Reject_An_Oversized_Page_Before_Calling_The_Store()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "unused", TaskBoardStatus.Open,
            new DateTimeOffset(2026, 8, 24, 22, 31, 0, TimeSpan.Zero), 0, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/task-boards?limit=101", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.LastLimit);
    }

    [Fact]
    public async Task List_Should_Reject_A_Nonpositive_Page_Before_Calling_The_Store()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "unused", TaskBoardStatus.Open, at, 0, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/task-boards?limit=0", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.ListCallCount);
    }

    [Fact]
    public async Task Find_Should_Return_The_Recursive_Board_Snapshot()
    {
        var at = new DateTimeOffset(2026, 8, 24, 22, 32, 0, TimeSpan.Zero);
        var task = new BoardTask(
            Guid.NewGuid(), "Root task", "description", TaskBoardStatus.Open,
            TaskPriority.High, 0, TaskOrdering.Ordered, null, 1, [], [], []);
        var snapshot = new TaskBoardSnapshot(
            Guid.NewGuid(), "Task board", "request", "plan",
            new TaskActor("steve", TaskActorKind.Human), at, at,
            TaskBoardStatus.Open, TaskOrdering.Ordered, [task]);
        var store = new StubStore(
            new TaskBoardSummary(
                snapshot.BoardId, snapshot.Title, snapshot.Status, at, 1, 0, 0),
            snapshot);
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/task-boards/{snapshot.BoardId:D}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(task.TaskId, body!.RootElement.GetProperty("tasks")[0]
            .GetProperty("taskId").GetGuid());
    }

    [Fact]
    public async Task Activity_Should_Return_The_Ordered_Board_Feed()
    {
        var boardId = Guid.NewGuid();
        var activity = new TaskBoardActivity(
            42, Guid.NewGuid(), boardId, null, null, TaskBoardActivityKind.BoardCreated,
            new TaskActor("steve", TaskActorKind.Human),
            new DateTimeOffset(2026, 8, 24, 22, 33, 0, TimeSpan.Zero),
            null, null, null);
        var store = new StubStore(
            new TaskBoardSummary(
                boardId, "board", TaskBoardStatus.Open, activity.OccurredAt, 0, 0, 0),
            activity: activity);
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/task-boards/{boardId:D}/activity?limit=5", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(42, Assert.Single(body!.RootElement.EnumerateArray())
            .GetProperty("sequence").GetInt64());
        Assert.Equal((boardId, 5), (store.LastActivityBoardId, store.LastActivityLimit));
    }

    [Fact]
    public async Task Activity_Should_Reject_An_Unbounded_Request_Before_Calling_The_Store()
    {
        var boardId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            boardId, "board", TaskBoardStatus.Open, at, 0, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/task-boards/{boardId:D}/activity?limit=501", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, store.LastActivityBoardId);
    }

    [Fact]
    public async Task Claim_Should_Forward_Actor_Version_And_Server_Time()
    {
        var taskId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.Open, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/task-boards/tasks/{taskId:D}/claim",
            new { expectedVersion = 3, actorId = "codex", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(body!.RootElement.GetProperty("updated").GetBoolean());
        Assert.Equal(
            (taskId, 3L, new TaskActor("codex", TaskActorKind.Agent), at),
            (store.LastClaimTaskId, store.LastClaimVersion,
                store.LastClaimActor, store.LastClaimedAt));
    }

    [Fact]
    public async Task AddTask_Should_Create_Under_The_Parent_As_The_Actor_And_Report_Conflict_When_Refused()
    {
        var boardId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(boardId, "board", TaskBoardStatus.Open, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync(
            $"/task-boards/{boardId:D}/tasks",
            new { title = "  new work ", parentTaskId = parentId, position = 4, criteria = new[] { "proven" }, actorId = "claude", actorKind = "Agent", detail = "why" },
            CancellationToken.None);
        var firstCall = (store.LastAddedBoardId, store.LastAddedParentId, store.LastAddedDraft!.Title,
            store.LastAddedDraft.Position, store.LastAddedDetail);
        var firstDraft = store.LastAddedDraft;
        var firstActor = store.LastAddedActor;
        store.AddResult = false;
        using var refused = await client.PostAsJsonAsync(
            $"/task-boards/{boardId:D}/tasks",
            new { title = "again", actorId = "claude", actorKind = "Agent" },
            CancellationToken.None);
        using var invalid = await client.PostAsJsonAsync(
            $"/task-boards/{boardId:D}/tasks",
            new { title = " ", actorId = "claude", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal((boardId, parentId, "new work", 4, "why"), firstCall);
        Assert.Equal("proven", Assert.Single(firstDraft.AcceptanceCriteria).Description);
        Assert.Equal(new TaskActor("claude", TaskActorKind.Agent), firstActor);
    }

    [Fact]
    public async Task Claim_Should_Return_Conflict_When_The_Compare_And_Set_Loses()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.Open, at, 1, 0, 0))
        {
            ClaimResult = false,
        };
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/task-boards/tasks/{Guid.NewGuid():D}/claim",
            new { expectedVersion = 1, actorId = "codex", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Claim_Should_Reject_A_Nonpositive_Version_Before_Calling_The_Store()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.Open, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/task-boards/tasks/{Guid.NewGuid():D}/claim",
            new { expectedVersion = 0, actorId = "codex", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, store.LastClaimTaskId);
    }

    [Fact]
    public async Task Claim_Should_Reject_A_Blank_Actor_Before_Calling_The_Store()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.Open, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/task-boards/tasks/{Guid.NewGuid():D}/claim",
            new { expectedVersion = 1, actorId = " ", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, store.LastClaimTaskId);
    }

    [Fact]
    public async Task Criterion_Should_Forward_Result_Actor_Version_And_Server_Time()
    {
        var criterionId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.Open, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/task-boards/criteria/{criterionId:D}",
            new
            {
                expectedVersion = 4,
                isSatisfied = true,
                actorId = "steve",
                actorKind = "Human",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            (criterionId, 4L, true, new TaskActor("steve", TaskActorKind.Human), at),
            (store.LastCriterionId, store.LastCriterionVersion,
                store.LastCriterionResult, store.LastCriterionActor,
                store.LastCriterionChangedAt));
    }

    [Fact]
    public async Task Complete_Should_Forward_Actor_Version_And_Server_Time()
    {
        var taskId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.InProgress, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/task-boards/tasks/{taskId:D}/complete",
            new { expectedVersion = 5, actorId = "codex", actorKind = "Agent" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            (taskId, 5L, new TaskActor("codex", TaskActorKind.Agent), at),
            (store.LastCompletedTaskId, store.LastCompletedVersion,
                store.LastCompletedActor, store.LastCompletedAt));
    }

    [Fact]
    public async Task Status_Should_Forward_State_Detail_Actor_Version_And_Server_Time()
    {
        var taskId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.InProgress, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/task-boards/tasks/{taskId:D}/status",
            new
            {
                expectedVersion = 6,
                status = "Blocked",
                detail = "waiting for migration",
                actorId = "codex",
                actorKind = "Agent",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            (taskId, 6L, TaskBoardStatus.Blocked, "waiting for migration",
                new TaskActor("codex", TaskActorKind.Agent), at),
            (store.LastStatusTaskId, store.LastStatusVersion, store.LastStatus,
                store.LastStatusDetail, store.LastStatusActor, store.LastStatusChangedAt));
    }

    [Fact]
    public async Task Status_Should_Reject_A_Blank_Detail_Before_Calling_The_Store()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.InProgress, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/task-boards/tasks/{Guid.NewGuid():D}/status",
            new
            {
                expectedVersion = 2,
                status = "Blocked",
                detail = " ",
                actorId = "codex",
                actorKind = "Agent",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, store.LastStatusTaskId);
    }

    [Fact]
    public async Task Status_Should_Reject_Done_Because_Completion_Has_Separate_Gates()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "board", TaskBoardStatus.InProgress, at, 1, 0, 0));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/task-boards/tasks/{Guid.NewGuid():D}/status",
            new
            {
                expectedVersion = 2,
                status = "Done",
                detail = "attempted bypass",
                actorId = "codex",
                actorKind = "Agent",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, store.LastStatusTaskId);
    }

    [Fact]
    public async Task Plan_Should_Persist_A_Planner_Result_With_Server_Time()
    {
        var requestId = Guid.NewGuid();
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "unused", TaskBoardStatus.Open, at, 0, 0, 0));
        var planner = new StubPlanner();
        await using var factory = CreateFactory(store, planner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/task-boards/plan",
            new
            {
                requestId,
                featureRequest = "Build a collaborative task board",
                actorId = "steve",
                actorKind = "Human",
                planner = "Local",
                privacy = "LocalOnly",
                origin = "UserTurn",
            },
            CancellationToken.None);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal($"/task-boards/{requestId:D}", response.Headers.Location?.OriginalString);
        Assert.Equal(at, Assert.IsType<TaskBoardDraft>(store.CreatedDraft).CreatedAt);
        Assert.Equal(requestId, store.CreatedDraft.BoardId);
    }

    [Fact]
    public async Task Plan_Should_Reject_A_Blank_Request_Before_Calling_The_Planner()
    {
        var store = new StubStore(new TaskBoardSummary(
            Guid.NewGuid(), "unused", TaskBoardStatus.Open, at, 0, 0, 0));
        var planner = new StubPlanner();
        await using var factory = CreateFactory(store, planner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/task-boards/plan",
            new
            {
                requestId = Guid.NewGuid(),
                featureRequest = " ",
                actorId = "steve",
                actorKind = "Human",
                planner = "Local",
                privacy = "LocalOnly",
                origin = "UserTurn",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, planner.CallCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        StubStore store,
        IFeaturePlanner? planner = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITaskBoardStore>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<ITaskBoardStore>(store);
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(at));
                if (planner is not null)
                {
                    services.RemoveAll<IFeaturePlanner>();
                    services.RemoveAll<FeaturePlanningService>();
                    services.AddSingleton<IFeaturePlanner>(planner);
                    services.AddSingleton<FeaturePlanningService>();
                }
            }));
    }

    private sealed class StubStore(
        TaskBoardSummary summary,
        TaskBoardSnapshot? snapshot = null,
        TaskBoardActivity? activity = null) : ITaskBoardStore
    {
        internal int LastLimit { get; private set; }

        internal int ListCallCount { get; private set; }

        internal Guid LastActivityBoardId { get; private set; }

        internal int LastActivityLimit { get; private set; }

        internal Guid LastClaimTaskId { get; private set; }

        internal long LastClaimVersion { get; private set; }

        internal TaskActor? LastClaimActor { get; private set; }

        internal DateTimeOffset LastClaimedAt { get; private set; }

        internal bool ClaimResult { get; init; } = true;

        internal Guid LastCriterionId { get; private set; }

        internal long LastCriterionVersion { get; private set; }

        internal bool LastCriterionResult { get; private set; }

        internal TaskActor? LastCriterionActor { get; private set; }

        internal DateTimeOffset LastCriterionChangedAt { get; private set; }

        internal Guid LastCompletedTaskId { get; private set; }

        internal long LastCompletedVersion { get; private set; }

        internal TaskActor? LastCompletedActor { get; private set; }

        internal DateTimeOffset LastCompletedAt { get; private set; }

        internal bool AddResult { get; set; } = true;

        internal Guid LastAddedBoardId { get; private set; }

        internal Guid? LastAddedParentId { get; private set; }

        internal BoardTaskDraft? LastAddedDraft { get; private set; }

        internal TaskActor? LastAddedActor { get; private set; }

        internal string? LastAddedDetail { get; private set; }

        internal Guid LastStatusTaskId { get; private set; }

        internal long LastStatusVersion { get; private set; }

        internal TaskBoardStatus LastStatus { get; private set; }

        internal string? LastStatusDetail { get; private set; }

        internal TaskActor? LastStatusActor { get; private set; }

        internal DateTimeOffset LastStatusChangedAt { get; private set; }

        internal TaskBoardDraft? CreatedDraft { get; private set; }

        public Task CreateAsync(TaskBoardDraft draft, CancellationToken cancellationToken)
        {
            this.CreatedDraft = draft;
            return Task.CompletedTask;
        }

        public Task<TaskBoardSnapshot?> FindAsync(
            Guid boardId,
            CancellationToken cancellationToken) => Task.FromResult(
                snapshot?.BoardId == boardId ? snapshot : null);

        public async IAsyncEnumerable<TaskBoardSummary> ListRecentAsync(
            int limit,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            this.ListCallCount++;
            this.LastLimit = limit;
            yield return summary;
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public Task<bool> TryAddTaskAsync(
            Guid boardId,
            Guid? parentTaskId,
            BoardTaskDraft draft,
            TaskActor actor,
            DateTimeOffset addedAt,
            string? detail,
            CancellationToken cancellationToken)
        {
            this.LastAddedBoardId = boardId;
            this.LastAddedParentId = parentTaskId;
            this.LastAddedDraft = draft;
            this.LastAddedActor = actor;
            this.LastAddedDetail = detail;
            return Task.FromResult(this.AddResult);
        }

        public Task<bool> TryClaimAsync(
            Guid taskId,
            long expectedVersion,
            TaskActor actor,
            DateTimeOffset claimedAt,
            string? detail,
            CancellationToken cancellationToken)
        {
            this.LastClaimTaskId = taskId;
            this.LastClaimVersion = expectedVersion;
            this.LastClaimActor = actor;
            this.LastClaimedAt = claimedAt;
            return Task.FromResult(this.ClaimResult);
        }

        public Task<bool> TrySetCriterionAsync(
            Guid criterionId,
            long expectedTaskVersion,
            bool isSatisfied,
            TaskActor actor,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            this.LastCriterionId = criterionId;
            this.LastCriterionVersion = expectedTaskVersion;
            this.LastCriterionResult = isSatisfied;
            this.LastCriterionActor = actor;
            this.LastCriterionChangedAt = changedAt;
            return Task.FromResult(true);
        }

        public Task<bool> TryCompleteAsync(
            Guid taskId,
            long expectedVersion,
            TaskActor actor,
            DateTimeOffset completedAt,
            string? detail,
            CancellationToken cancellationToken)
        {
            this.LastCompletedTaskId = taskId;
            this.LastCompletedVersion = expectedVersion;
            this.LastCompletedActor = actor;
            this.LastCompletedAt = completedAt;
            return Task.FromResult(true);
        }

        public Task<bool> TrySetStatusAsync(
            Guid taskId,
            long expectedVersion,
            TaskBoardStatus status,
            TaskActor actor,
            string detail,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            this.LastStatusTaskId = taskId;
            this.LastStatusVersion = expectedVersion;
            this.LastStatus = status;
            this.LastStatusActor = actor;
            this.LastStatusDetail = detail;
            this.LastStatusChangedAt = changedAt;
            return Task.FromResult(true);
        }

        public async IAsyncEnumerable<TaskBoardActivity> ActivityAsync(
            Guid boardId,
            int limit,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            this.LastActivityBoardId = boardId;
            this.LastActivityLimit = limit;
            if (activity?.BoardId == boardId)
            {
                yield return activity;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class StubPlanner : IFeaturePlanner
    {
        public int CallCount { get; private set; }

        public FeaturePlannerKind Kind => FeaturePlannerKind.Local;

        public Task<FeaturePlanProposal> PlanAsync(
            FeaturePlanningRequest request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(new FeaturePlanProposal(
                "Task board", "Implement the board", TaskOrdering.Ordered,
                [new PlannedTask(
                    "O1", "Implement", "Build it", TaskPriority.High, 0,
                    TaskOrdering.Ordered, [], ["tests pass"], [])]));
        }
    }
}
