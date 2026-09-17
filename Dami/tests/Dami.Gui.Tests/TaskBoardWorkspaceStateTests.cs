using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TaskBoardWorkspaceStateTests
{
    [Fact]
    public void Present_Should_Keep_Selection_Current_And_Ignore_Previous_Board_Replies()
    {
        var state = new TaskBoardPanelState();
        var task = Task("Build shelves");
        var board = Board(task);
        state.SelectBoard(board.BoardId);
        state.Present(board);
        state.Selected = Assert.Single(state.Tasks);
        state.Present(board with { Tasks = [task with { Version = 2 }] });
        var version = state.Selected?.Version;
        state.SelectBoard(Guid.NewGuid());
        state.Present(board);

        Assert.Equal((2L, 0, false), (version, state.Tasks.Count, state.HasSelection));
    }

    [Theory]
    [InlineData(TaskBoardStatus.Open, false, false, "Start this task to begin tracking work.")]
    [InlineData(TaskBoardStatus.InProgress, false, false, "Verify 1 remaining requirement before completing.")]
    [InlineData(TaskBoardStatus.InProgress, true, true, "All requirements are verified. Ready to complete.")]
    public void Completion_Should_Explain_The_Next_Step(
        TaskBoardStatus status, bool verified, bool canComplete, string nextStep)
    {
        var task = Task("Build shelves") with
        {
            Status = status,
            AcceptanceCriteria = [new AcceptanceCriterion(
                Guid.NewGuid(), "Fits the space", 0, verified, null, null)],
        };
        var node = TaskBoardTaskNode.From(task);

        Assert.Equal((canComplete, nextStep), (node.CanComplete, node.NextStep));
    }

    [Theory]
    [InlineData(TaskBoardStatus.Open, TaskBoardStatus.Done, false, false)]
    [InlineData(TaskBoardStatus.Cancelled, TaskBoardStatus.Done, false, false)]
    [InlineData(TaskBoardStatus.Done, TaskBoardStatus.Open, true, false)]
    [InlineData(TaskBoardStatus.Done, TaskBoardStatus.Cancelled, true, true)]
    public void Prerequisites_And_Children_Should_Gate_Readiness(
        TaskBoardStatus dependencyStatus, TaskBoardStatus childStatus,
        bool canStart, bool canComplete)
    {
        var dependency = Task("Measure the wall") with { Status = dependencyStatus };
        var task = Task("Build shelves") with
        {
            PrerequisiteTaskIds = [dependency.TaskId],
            SubTasks = [Task("Cut timber") with { Status = childStatus }],
        };
        var ready = TaskBoardTaskNode.FromBoard([dependency, task])[1];
        var started = TaskBoardTaskNode.FromBoard(
            [dependency, task with { Status = TaskBoardStatus.InProgress }])[1];

        Assert.Equal((canStart, canComplete, "Measure the wall"),
            (ready.CanClaim, started.CanComplete, ready.Dependencies[0].Title));
    }

    internal static BoardTask Task(string title) => new(
        Guid.NewGuid(), title, "Build and verify", TaskBoardStatus.Open,
        TaskPriority.Normal, 0, TaskOrdering.Ordered, null, 1, [], [], []);

    internal static TaskBoardSnapshot Board(params BoardTask[] tasks) => new(
        Guid.NewGuid(), "Workshop", "Make storage", "Measure, build, install",
        new TaskActor("steve", TaskActorKind.Human), default, default,
        TaskBoardStatus.Open, TaskOrdering.Ordered, tasks);
}
