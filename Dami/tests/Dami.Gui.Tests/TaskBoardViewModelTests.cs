using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TaskBoardViewModelTests
{
    [Fact]
    public void From_Should_Preserve_Recursive_Tasks_And_Criterion_Versions()
    {
        var criterion = new AcceptanceCriterion(
            Guid.NewGuid(), "observed live", 0, false, null, null);
        var child = new BoardTask(
            Guid.NewGuid(), "child", "nested", TaskBoardStatus.Open,
            TaskPriority.Normal, 0, TaskOrdering.Ordered, null, 1, [], [], []);
        var root = new BoardTask(
            Guid.NewGuid(), "root", "parent", TaskBoardStatus.InProgress,
            TaskPriority.High, 0, TaskOrdering.Ordered,
            new TaskClaim(new TaskActor("codex", TaskActorKind.Agent),
                new DateTimeOffset(2026, 8, 24, 23, 45, 0, TimeSpan.Zero)),
            9, [], [criterion], [child]);

        var node = TaskBoardTaskNode.From(root);

        Assert.Equal((root.TaskId, root.Status, "codex"),
            (node.TaskId, node.Status, node.ClaimedBy));
        Assert.Equal(child.TaskId, Assert.Single(node.SubTasks).TaskId);
        var criterionNode = Assert.Single(node.Criteria);
        Assert.Equal((criterion.CriterionId, 9L, false),
            (criterionNode.CriterionId, criterionNode.ExpectedTaskVersion,
                criterionNode.IsSatisfied));
    }
}
