using Dami.Contracts.TaskBoard;
using Dami.Contracts.Events;
using Dami.Contracts.Context;
using Dami.Core.TaskBoard;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class FeaturePlanningServiceTests
{
    private static readonly DateTimeOffset requestedAt =
        new(2026, 8, 24, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlanAsync_Should_Map_One_Complete_Recursive_Proposal_Into_One_Store_Call()
    {
        var proposal = CreateProposal();
        var planner = new StubPlanner(FeaturePlannerKind.Local, proposal);
        var store = new RecordingTaskBoardStore();
        var service = new FeaturePlanningService([planner], store);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build a shared board", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);

        var boardId = await service.PlanAsync(request, CancellationToken.None);

        Assert.Equal(request.RequestId, boardId);
        var draft = Assert.Single(store.Created);
        Assert.Equal((request.RequestId, request.FeatureRequest, proposal.Plan),
            (draft.BoardId, draft.FeatureRequest, draft.Plan));
        Assert.Equal(
            new TaskBoardPlanningContext(request.Planner, request.Privacy, request.Origin),
            draft.PlanningContext);
        var root = Assert.Single(draft.Tasks);
        var foundation = Assert.Single(root.SubTasks, task => task.Title == "Foundation");
        var api = Assert.Single(root.SubTasks, task => task.Title == "API");
        Assert.Equal(foundation.TaskId, Assert.Single(api.PrerequisiteTaskIds));
        Assert.Equal("API responds", Assert.Single(api.AcceptanceCriteria).Description);
    }

    [Fact]
    public async Task PlanAsync_Should_Return_An_Existing_Request_Without_Replanning()
    {
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build a shared board", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);
        var planner = new StubPlanner(FeaturePlannerKind.Local, CreateProposal());
        var store = new ExistingTaskBoardStore(new TaskBoardSnapshot(
            request.RequestId, "Shared task board", request.FeatureRequest, "existing plan",
            request.RequestedBy, request.RequestedAt, request.RequestedAt,
            TaskBoardStatus.InProgress, TaskOrdering.Ordered, [],
            new TaskBoardPlanningContext(request.Planner, request.Privacy, request.Origin)));
        var service = new FeaturePlanningService([planner], store);

        var boardId = await service.PlanAsync(request, CancellationToken.None);

        Assert.Equal(request.RequestId, boardId);
        Assert.Equal(0, planner.CallCount);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_A_Reused_Request_Id_With_Different_Content()
    {
        var requestId = Guid.NewGuid();
        var request = new FeaturePlanningRequest(
            requestId, "Different request", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);
        var planner = new StubPlanner(FeaturePlannerKind.Local, CreateProposal());
        var store = new ExistingTaskBoardStore(new TaskBoardSnapshot(
            requestId, "Existing", "Original request", "existing plan",
            request.RequestedBy, request.RequestedAt, request.RequestedAt,
            TaskBoardStatus.Open, TaskOrdering.Ordered, [],
            new TaskBoardPlanningContext(request.Planner, request.Privacy, request.Origin)));
        var service = new FeaturePlanningService([planner], store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Equal(0, planner.CallCount);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_A_Reused_Request_Id_With_Different_Provenance()
    {
        var requestId = Guid.NewGuid();
        var actor = new TaskActor("steve", TaskActorKind.Human);
        var request = new FeaturePlanningRequest(
            requestId, "Same request", actor, requestedAt, FeaturePlannerKind.Frontier,
            PrivacyClass.Egressable, ExecutionOrigin.UserTurn);
        var planner = new StubPlanner(FeaturePlannerKind.Frontier, CreateProposal());
        var store = new ExistingTaskBoardStore(new TaskBoardSnapshot(
            requestId, "Existing", request.FeatureRequest, "existing plan", actor,
            requestedAt, requestedAt, TaskBoardStatus.Open, TaskOrdering.Ordered, [],
            new TaskBoardPlanningContext(
                FeaturePlannerKind.Local, PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn)));
        var service = new FeaturePlanningService([planner], store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Equal(0, planner.CallCount);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_A_Proposal_Without_Tasks()
    {
        var planner = new StubPlanner(
            FeaturePlannerKind.Local,
            new FeaturePlanProposal("Empty", "There is no work.", TaskOrdering.Ordered, []));
        var store = new RecordingTaskBoardStore();
        var service = new FeaturePlanningService([planner], store);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build something", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_A_Task_With_A_Null_Subtask_Collection()
    {
        var invalid = new PlannedTask(
            "invalid", "Invalid", "Malformed model output", TaskPriority.Normal, 0,
            TaskOrdering.Ordered, [], [], null!);
        var planner = new StubPlanner(
            FeaturePlannerKind.Local,
            new FeaturePlanProposal("Invalid", "Reject it.", TaskOrdering.Ordered, [invalid]));
        var store = new RecordingTaskBoardStore();
        var service = new FeaturePlanningService([planner], store);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build something", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_A_Task_With_Null_Acceptance_Criteria()
    {
        var invalid = new PlannedTask(
            "invalid", "Invalid", "Malformed model output", TaskPriority.Normal, 0,
            TaskOrdering.Ordered, [], null!, []);
        var planner = new StubPlanner(
            FeaturePlannerKind.Local,
            new FeaturePlanProposal("Invalid", "Reject it.", TaskOrdering.Ordered, [invalid]));
        var store = new RecordingTaskBoardStore();
        var service = new FeaturePlanningService([planner], store);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build something", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task PlanAsync_Should_Reject_More_Than_1024_Tasks()
    {
        var tasks = Enumerable.Range(0, 1025)
            .Select(index => new PlannedTask(
                $"task-{index}", $"Task {index}", string.Empty, TaskPriority.Normal,
                index, TaskOrdering.Ordered, [], [], []))
            .ToArray();
        var planner = new StubPlanner(
            FeaturePlannerKind.Local,
            new FeaturePlanProposal("Too large", "Reject it.", TaskOrdering.Ordered, tasks));
        var store = new RecordingTaskBoardStore();
        var service = new FeaturePlanningService([planner], store);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build too much", new TaskActor("steve", TaskActorKind.Human),
            requestedAt, FeaturePlannerKind.Local, PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PlanAsync(request, CancellationToken.None));

        Assert.Empty(store.Created);
    }

    private static FeaturePlanProposal CreateProposal()
    {
        var foundation = new PlannedTask(
            "foundation", "Foundation", "Persist it", TaskPriority.Critical, 0,
            TaskOrdering.Ordered, [], [], []);
        var api = new PlannedTask(
            "api", "API", "Expose it", TaskPriority.High, 0,
            TaskOrdering.Ordered, ["foundation"], ["API responds"], []);
        var root = new PlannedTask(
            "root", "Task board", "Deliver vertically", TaskPriority.Critical, 0,
            TaskOrdering.Ordered, [], [], [foundation, api]);
        return new FeaturePlanProposal(
            "Shared task board", "Persist, expose, and render.", TaskOrdering.Ordered, [root]);
    }

    private sealed class StubPlanner : IFeaturePlanner
    {
        private readonly FeaturePlanProposal proposal;

        internal StubPlanner(FeaturePlannerKind kind, FeaturePlanProposal proposal)
        {
            this.Kind = kind;
            this.proposal = proposal;
        }

        public FeaturePlannerKind Kind { get; }

        internal int CallCount { get; private set; }

        public Task<FeaturePlanProposal> PlanAsync(
            FeaturePlanningRequest request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(this.proposal);
        }
    }

    private sealed class RecordingTaskBoardStore : TaskBoardStoreStub
    {
        internal List<TaskBoardDraft> Created { get; } = [];

        public override Task CreateAsync(
            TaskBoardDraft draft,
            CancellationToken cancellationToken)
        {
            this.Created.Add(draft);
            return Task.CompletedTask;
        }
    }

    private sealed class ExistingTaskBoardStore : TaskBoardStoreStub
    {
        private readonly TaskBoardSnapshot snapshot;

        internal ExistingTaskBoardStore(TaskBoardSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        internal List<TaskBoardDraft> Created { get; } = [];

        public override Task CreateAsync(
            TaskBoardDraft draft,
            CancellationToken cancellationToken)
        {
            this.Created.Add(draft);
            return Task.CompletedTask;
        }

        public override Task<TaskBoardSnapshot?> FindAsync(
            Guid boardId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TaskBoardSnapshot?>(this.snapshot);
        }
    }
}
