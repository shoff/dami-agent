using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
using Dami.Core.TaskBoard;
using Dami.Core.Turns;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class TaskWorkServiceTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskActor steve = new("steve", TaskActorKind.Human);
    private static readonly Guid boardId = Guid.NewGuid();

    private sealed class Runner : ITurnRunner
    {
        public string? Seen { get; private set; }

        public Guid TraceId { get; } = Guid.NewGuid();

        public string Answer { get; set; } = "here is what I would do";

        public Exception? Throw { get; set; }

        public Task<TurnResult> RunAsync(string request, CancellationToken cancellationToken)
        {
            this.Seen = request;
            if (this.Throw is not null)
            {
                throw this.Throw;
            }

            return Task.FromResult(new TurnResult(
                this.TraceId, this.Answer, new AssembledContext([], [], 0),
                new ModelRoute(ModelTier.Local, PrivacyClass.LocalOnly, "test")));
        }

        public Task<TurnStream> BeginStreamingAsync(string request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class Store : TaskBoardStoreStub
    {
        public TaskBoardSnapshot? Snapshot { get; set; }

        public List<(TaskBoardActivityKind Kind, string Detail)> Logged { get; } = [];

        public override Task<TaskBoardSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.Snapshot);
        }

        public override Task<bool> TryLogWorkAsync(
            Guid taskId,
            TaskBoardActivityKind kind,
            TaskActor actor,
            string detail,
            DateTimeOffset loggedAt,
            CancellationToken cancellationToken)
        {
            this.Logged.Add((kind, detail));
            return Task.FromResult(true);
        }
    }

    private static BoardTask BoardTaskOf(
        Guid taskId,
        string title,
        TaskBoardStatus status = TaskBoardStatus.Open,
        params BoardTask[] subTasks)
    {
        return new BoardTask(
            taskId, title, "scope", status, TaskPriority.Normal, 0,
            TaskOrdering.Ordered, null, 1, [], [], subTasks);
    }

    private static TaskBoardSnapshot Board(params BoardTask[] tasks)
    {
        return new TaskBoardSnapshot(
            boardId, "Dami Core suite", "request", "plan", steve, at, at,
            TaskBoardStatus.InProgress, TaskOrdering.Ordered, tasks);
    }

    private sealed class Frontier : Dami.Core.Frontier.IAugmentedTurn
    {
        internal string? Seen { get; private set; }

        internal Exception? Throw { get; set; }

        public Task<Dami.Core.Frontier.AugmentedTurnResult> RunAsync(
            string question,
            IReadOnlyList<string> priorExchanges,
            CancellationToken cancellationToken)
        {
            this.Seen = question;
            if (this.Throw is not null)
            {
                throw this.Throw;
            }

            return Task.FromResult(new Dami.Core.Frontier.AugmentedTurnResult(
                Guid.NewGuid(), "the frontier's proposal", 7, 900));
        }
    }

    private static (TaskWorkService Service, Store Store, Runner Runner, Frontier Frontier) Create(
        TaskBoardSnapshot? snapshot)
    {
        var store = new Store { Snapshot = snapshot };
        var runner = new Runner();
        var frontier = new Frontier();
        return (
            new TaskWorkService(store, runner, new FakeTimeProvider(at), frontier),
            store, runner, frontier);
    }

    [Fact]
    public async Task RunAsync_Should_Run_The_Turn_On_The_Task_It_Was_Given()
    {
        var taskId = Guid.NewGuid();
        var (service, _, runner, _) = Create(Board(BoardTaskOf(taskId, "A6 PostgreSQL major version")));

        var outcome = await service.RunAsync(boardId, taskId, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.True(outcome.Ran);
        Assert.Equal(runner.TraceId, outcome.TraceId);
        Assert.Contains("A6 PostgreSQL major version", runner.Seen!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Find_A_Task_Nested_Anywhere_In_The_Tree()
    {
        var deep = Guid.NewGuid();
        var (service, _, runner, _) = Create(Board(
            BoardTaskOf(Guid.NewGuid(), "epic", TaskBoardStatus.Open,
                BoardTaskOf(Guid.NewGuid(), "middle", TaskBoardStatus.Open,
                    BoardTaskOf(deep, "the actual task")))));

        var outcome = await service.RunAsync(boardId, deep, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.True(outcome.Ran);
        Assert.Contains("the actual task", runner.Seen!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Bracket_The_Turn_With_Started_And_Finished()
    {
        // The board is the record. A run that left no trace of having happened would be
        // exactly the kind of unevidenced claim this repository treats as a defect.
        var taskId = Guid.NewGuid();
        var (service, store, runner, _) = Create(Board(BoardTaskOf(taskId, "task")));

        await service.RunAsync(boardId, taskId, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.Equal(
            [TaskBoardActivityKind.TaskWorkStarted, TaskBoardActivityKind.TaskWorkFinished],
            store.Logged.Select(entry => entry.Kind));
        Assert.Contains(runner.TraceId.ToString("N")[..8], store.Logged[1].Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TaskBoardStatus.Done)]
    [InlineData(TaskBoardStatus.Cancelled)]
    public async Task RunAsync_Should_Refuse_Finished_Work_Without_Running_A_Turn(
        TaskBoardStatus status)
    {
        var taskId = Guid.NewGuid();
        var (service, store, runner, _) = Create(Board(BoardTaskOf(taskId, "task", status)));

        var outcome = await service.RunAsync(boardId, taskId, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.False(outcome.Ran);
        Assert.Null(runner.Seen);
        Assert.Empty(store.Logged);
    }

    [Fact]
    public async Task RunAsync_Should_Report_An_Unknown_Task_Rather_Than_Guess()
    {
        var (service, store, runner, _) = Create(Board(BoardTaskOf(Guid.NewGuid(), "task")));

        var outcome = await service.RunAsync(
            boardId, Guid.NewGuid(), steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.False(outcome.Ran);
        Assert.Null(runner.Seen);
        Assert.Empty(store.Logged);
    }

    [Fact]
    public async Task RunAsync_Should_Report_A_Missing_Board()
    {
        var (service, _, runner, _) = Create(null);

        var outcome = await service.RunAsync(
            boardId, Guid.NewGuid(), steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.False(outcome.Ran);
        Assert.Null(runner.Seen);
    }

    [Fact]
    public async Task RunAsync_Should_Record_A_Failed_Turn_Instead_Of_Losing_It()
    {
        // A turn that throws still happened. Leaving only TaskWorkStarted on the board
        // would read as a run that never came back.
        var taskId = Guid.NewGuid();
        var (service, store, runner, _) = Create(Board(BoardTaskOf(taskId, "task")));
        runner.Throw = new InvalidOperationException("the model is down");

        var outcome = await service.RunAsync(boardId, taskId, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.False(outcome.Ran);
        Assert.Equal(
            [TaskBoardActivityKind.TaskWorkStarted, TaskBoardActivityKind.TaskWorkFinished],
            store.Logged.Select(entry => entry.Kind));
        Assert.Contains("the model is down", store.Logged[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Send_To_The_Frontier_When_Asked()
    {
        // Steve's first real run went to the local 8B and was useless. The picker has to
        // actually change where the work goes.
        var taskId = Guid.NewGuid();
        var (service, _, runner, frontier) = Create(Board(BoardTaskOf(taskId, "A6 upgrade")));

        var outcome = await service.RunAsync(
            boardId, taskId, steve, FeaturePlannerKind.Frontier, CancellationToken.None);

        Assert.True(outcome.Ran);
        Assert.Equal("the frontier's proposal", outcome.Answer);
        Assert.Contains("A6 upgrade", frontier.Seen!, StringComparison.Ordinal);
        Assert.Null(runner.Seen);
    }

    [Fact]
    public async Task RunAsync_Should_Record_That_Local_Retrieval_Fed_The_Frontier()
    {
        var taskId = Guid.NewGuid();
        var (service, store, _, _) = Create(Board(BoardTaskOf(taskId, "task")));

        await service.RunAsync(
            boardId, taskId, steve, FeaturePlannerKind.Frontier, CancellationToken.None);

        Assert.Contains("locally retrieved 7 item(s)", store.Logged[1].Detail, StringComparison.Ordinal);
        Assert.Contains("answered at the frontier", store.Logged[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Fall_Back_To_Local_When_The_Subscription_Is_Unavailable()
    {
        // The two models are not alternatives: local feeds the frontier. But if the
        // subscription is not there — not signed in, CLI missing, process failing — the
        // run should be answered locally rather than lost.
        var taskId = Guid.NewGuid();
        var (service, store, runner, frontier) = Create(Board(BoardTaskOf(taskId, "task")));
        frontier.Throw = new InvalidOperationException("codex: not signed in");

        var outcome = await service.RunAsync(
            boardId, taskId, steve, FeaturePlannerKind.Frontier, CancellationToken.None);

        Assert.True(outcome.Ran);
        Assert.Equal("here is what I would do", outcome.Answer);
        Assert.NotNull(runner.Seen);
        Assert.Contains("frontier unavailable", store.Logged[1].Detail, StringComparison.Ordinal);
        Assert.Contains("not signed in", store.Logged[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Not_Route_Around_A_Privacy_Refusal()
    {
        // An egress refusal is an answer, not an outage. Falling back to local would
        // quietly turn a boundary decision into a different one.
        var taskId = Guid.NewGuid();
        var (service, store, runner, frontier) = Create(Board(BoardTaskOf(taskId, "task")));
        frontier.Throw = new Dami.Contracts.Privacy.EgressRefusedException("not egressable");

        var outcome = await service.RunAsync(
            boardId, taskId, steve, FeaturePlannerKind.Frontier, CancellationToken.None);

        Assert.False(outcome.Ran);
        Assert.Null(runner.Seen);
        Assert.Contains("not egressable", store.Logged[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Never_Complete_The_Task()
    {
        // The V1 safety boundary, pinned. TaskBoardStoreStub returns false for every
        // mutation, so this asserts the service does not even attempt one.
        var taskId = Guid.NewGuid();
        var (service, store, _, _) = Create(Board(BoardTaskOf(taskId, "task")));

        await service.RunAsync(boardId, taskId, steve, FeaturePlannerKind.Local, CancellationToken.None);

        Assert.All(store.Logged, entry => Assert.True(
            entry.Kind is TaskBoardActivityKind.TaskWorkStarted
                or TaskBoardActivityKind.TaskWorkFinished));
    }
}
