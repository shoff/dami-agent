using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TaskBoardWorkspaceTests
{
    [Fact]
    public async Task Workspace_Should_Search_Active_Work_And_Show_The_Selected_Task()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var task = TaskBoardWorkspaceStateTests.Task("Build shelves") with
            {
                Status = TaskBoardStatus.InProgress,
            };
            var board = TaskBoardWorkspaceStateTests.Board(task,
                TaskBoardWorkspaceStateTests.Task("Completed shelf") with { Status = TaskBoardStatus.Done });
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.FindControl<TextBox>("TaskSearch")!.Text = "shelves";
            view.FindControl<ListBox>("TaskList")!.SelectedIndex = 0;

            Assert.Equal((1, task.TaskId, "Build shelves", false),
                (view.State.Tasks.Count, view.State.Selected?.TaskId,
                    view.FindControl<SelectableTextBlock>("TaskTitle")!.Text,
                    view.FindControl<Border>("PlanForm")!.IsVisible));
        });
    }
    [Fact]
    public async Task Entry_Should_Keep_The_Draft_And_Prevent_Duplicate_Submission_While_Busy()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new TaskBoardWorkspace();
            var board = TaskBoardWorkspaceStateTests.Board();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            var submitted = new List<TaskBoardEntry>();
            view.ActionRequested += (_, target) => submitted.Add((TaskBoardEntry)target!);
            view.Control<Button>("AddTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.Control<TextBox>("NewTaskTitle").Text = "Cut timber";
            view.Control<Button>("SaveTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.SetBusy(true);
            view.Control<Button>("SaveTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.SetBusy(false);
            view.Control<Button>("SaveTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal((2, true, board.BoardId, "Cut timber"),
                (submitted.Count, submitted[0].TaskId == submitted[1].TaskId,
                    submitted[0].BoardId, view.Control<TextBox>("NewTaskTitle").Text));
        });
    }
    [Fact]
    public async Task PlanSearch_Should_Filter_Choices_Without_Switching_The_Open_Plan()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var first = new TaskBoardSummary(Guid.NewGuid(), "Workshop", TaskBoardStatus.Open, default, 2, 0, 0);
            var second = first with { BoardId = Guid.NewGuid(), Title = "Garden" };
            var view = new TaskBoardWorkspace();
            view.PresentBoards([first, second]);
            view.Control<TextBox>("PlanSearch").Text = "garden";
            view.PresentBoards([first, second]);

            Assert.Equal((1, first.BoardId),
                (view.Control<ListBox>("BoardPicker").ItemCount, view.State.BoardId));
        });
    }
    [Fact]
    public async Task Selection_Should_Keep_Requirement_And_Reason_Drafts_With_Their_Task()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var first = TaskBoardWorkspaceStateTests.Task("First");
            var second = TaskBoardWorkspaceStateTests.Task("Second");
            var board = TaskBoardWorkspaceStateTests.Board(first, second);
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.FocusTask(first.TaskId);
            view.Control<TextBox>("NewCriterion").Text = "Measure the fit";
            view.Control<TextBox>("StatusDetail").Text = "Waiting for materials";
            view.FocusTask(second.TaskId);
            var secondReason = view.Control<TextBox>("StatusDetail").Text;
            view.FocusTask(first.TaskId);

            Assert.Equal((string.Empty, "Waiting for materials", "Measure the fit"),
                (secondReason, view.Control<TextBox>("StatusDetail").Text, view.Control<TextBox>("NewCriterion").Text));
        });
    }
    [Theory]
    [InlineData("steve", TaskActorKind.Human, true)]
    [InlineData("codex", TaskActorKind.Agent, false)]
    [InlineData("steve", TaskActorKind.Agent, false)]
    public async Task Completion_Should_Be_Enabled_Only_For_The_Current_Owner(string owner, TaskActorKind kind, bool enabled)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var task = TaskBoardWorkspaceStateTests.Task("Finish shelves") with
            {
                Status = TaskBoardStatus.InProgress,
                Claim = new TaskClaim(new TaskActor(owner, kind), default),
            };
            var board = TaskBoardWorkspaceStateTests.Board(task);
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.FocusTask(task.TaskId);

            Assert.Equal(enabled, view.Control<Button>("CompleteTask").IsEnabled);
        });
    }
    [Fact]
    public async Task BoardSelection_Should_Clear_The_Previous_Plan_While_Loading()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var board = TaskBoardWorkspaceStateTests.Board(TaskBoardWorkspaceStateTests.Task("Build"));
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.SelectBoard(Guid.NewGuid());

            Assert.Equal((string.Empty, string.Empty, 0),
                (view.Control<ReplyView>("PlanRequest").Text, view.Control<ReplyView>("PlanApproach").Text, view.State.AllCount));
        });
    }
    [Fact]
    public async Task CodeCopy_Should_Forward_The_Exact_Code_From_The_Reader()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new TaskBoardWorkspace();
            (string? Action, object? Value) captured = (null, null);
            view.ActionRequested += (action, value) => captured = (action, value);
            var button = new Button { Tag = "copy-code", CommandParameter = "echo checked\n" };
            view.OnContentAction(null, new RoutedEventArgs(Button.ClickEvent, button));

            Assert.Equal(("CopyCode", "echo checked\n"), (captured.Action, captured.Value));
        });
    }
    [Fact]
    public async Task Subtask_Should_Use_The_Selected_Parent_After_An_Empty_Form_Was_Closed()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var parent = TaskBoardWorkspaceStateTests.Task("Build shelves");
            var board = TaskBoardWorkspaceStateTests.Board(parent);
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.Control<Button>("AddTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.Control<Button>("CloseTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FocusTask(parent.TaskId);
            var button = new Button { Tag = "AddSubtask", DataContext = view.State.Selected };
            view.OnContentAction(null, new RoutedEventArgs(Button.ClickEvent, button));
            view.Control<TextBox>("NewTaskTitle").Text = "Cut timber";
            TaskBoardEntry? submitted = null;
            view.ActionRequested += (_, target) => submitted = (TaskBoardEntry?)target;
            view.Control<Button>("SaveTask").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(parent.TaskId, submitted?.ParentTaskId);
        });
    }
    [Fact]
    public async Task LeafTask_Should_Hide_Empty_Related_Task_Sections()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var task = TaskBoardWorkspaceStateTests.Task("Build shelves");
            var board = TaskBoardWorkspaceStateTests.Board(task);
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.FocusTask(task.TaskId);

            Assert.Equal((false, false),
                (view.Control<Expander>("SubtasksSection").IsVisible, view.Control<Expander>("DependenciesSection").IsVisible));
        });
    }
}
