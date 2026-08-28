using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class BoardFilterTests
{
    private static BoardTask Task(
        string title,
        TaskBoardStatus status,
        string description = "",
        params BoardTask[] subTasks)
    {
        return new BoardTask(
            Guid.NewGuid(), title, description, status, TaskPriority.Normal, 0,
            TaskOrdering.Ordered, null, 1, [], [], subTasks);
    }

    private static IReadOnlyList<TaskBoardTaskNode> Tree(params BoardTask[] roots)
    {
        return roots.Select(TaskBoardTaskNode.From).ToList();
    }

    [Fact]
    public void All_Should_Return_The_Roots_Untouched_So_The_Tree_Still_Nests()
    {
        var roots = Tree(Task("Epic", TaskBoardStatus.Open, "", Task("Child", TaskBoardStatus.Open)));

        var filtered = BoardFilter.Apply(roots, BoardView.All);

        var only = Assert.Single(filtered);
        Assert.Equal("Epic", only.Title);
        Assert.Single(only.SubTasks);
    }

    [Fact]
    public void NeedsYou_Should_Find_Steve_Markers_Nested_Anywhere_In_The_Tree()
    {
        // The items that actually want Steve are leaves buried several levels down in a
        // 212-task import. A filter that only looked at roots would return nothing.
        var roots = Tree(Task("Epic", TaskBoardStatus.Open, "epic",
            Task("Middle", TaskBoardStatus.Open, "middle",
                Task("B9 retention", TaskBoardStatus.Open, "ADR-0012 [STEVE] needs approval"))));

        var filtered = BoardFilter.Apply(roots, BoardView.NeedsYou);

        var only = Assert.Single(filtered);
        Assert.Equal("B9 retention", only.Title);
    }

    [Fact]
    public void NeedsYou_Should_Find_Them_Whatever_Their_Status()
    {
        // The existing sidebar digest only caught Blocked tasks naming Steve, which is
        // why most of his open decisions never appeared anywhere in the UI.
        var roots = Tree(
            Task("open one", TaskBoardStatus.Open, "[STEVE] decide"),
            Task("blocked one", TaskBoardStatus.Blocked, "[STEVE] decide"),
            Task("in progress", TaskBoardStatus.InProgress, "[STEVE] decide"));

        var filtered = BoardFilter.Apply(roots, BoardView.NeedsYou);

        Assert.Equal(3, filtered.Count);
    }

    [Theory]
    [InlineData(TaskBoardStatus.Done)]
    [InlineData(TaskBoardStatus.Cancelled)]
    public void NeedsYou_Should_Ignore_Finished_Work(TaskBoardStatus status)
    {
        var roots = Tree(Task("settled", status, "[STEVE] was asked, long since answered"));

        Assert.Empty(BoardFilter.Apply(roots, BoardView.NeedsYou));
    }

    [Fact]
    public void Open_And_Blocked_Should_Flatten_To_Just_That_Status()
    {
        var roots = Tree(Task("Epic", TaskBoardStatus.Done, "",
            Task("a", TaskBoardStatus.Open),
            Task("b", TaskBoardStatus.Blocked),
            Task("c", TaskBoardStatus.Open)));

        Assert.Equal(2, BoardFilter.Apply(roots, BoardView.Open).Count);
        Assert.Single(BoardFilter.Apply(roots, BoardView.Blocked));
    }

    [Fact]
    public void Count_Should_Agree_With_Apply_For_The_Flattening_Views()
    {
        var roots = Tree(Task("Epic", TaskBoardStatus.Open, "[STEVE] one",
            Task("a", TaskBoardStatus.Open),
            Task("b", TaskBoardStatus.Blocked, "[STEVE] two")));

        foreach (var view in new[] { BoardView.NeedsYou, BoardView.Open, BoardView.Blocked })
        {
            Assert.Equal(BoardFilter.Apply(roots, view).Count, BoardFilter.Count(roots, view));
        }
    }

    [Fact]
    public void Count_For_All_Should_Be_Every_Task_Not_Every_Root()
    {
        // Counting roots put "All 15" beside "Open 20" on the live board — a total
        // smaller than one of its own parts.
        var roots = Tree(Task("Epic", TaskBoardStatus.Open, "",
            Task("a", TaskBoardStatus.Open),
            Task("b", TaskBoardStatus.Blocked, "", Task("b1", TaskBoardStatus.Open))));

        Assert.Single(BoardFilter.Apply(roots, BoardView.All));
        Assert.Equal(4, BoardFilter.Count(roots, BoardView.All));
        Assert.True(
            BoardFilter.Count(roots, BoardView.All) >= BoardFilter.Count(roots, BoardView.Open));
    }

    [Fact]
    public void Apply_Should_Tolerate_An_Empty_Board()
    {
        foreach (var view in Enum.GetValues<BoardView>())
        {
            Assert.Empty(BoardFilter.Apply([], view));
            Assert.Equal(0, BoardFilter.Count([], view));
        }
    }
}
