using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TaskBoardWorkspaceControllerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Add_Then_Refresh_And_Select_The_New_Task()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var board = TaskBoardWorkspaceStateTests.Board();
            var entry = new TaskBoardEntry(board.BoardId, null, Guid.NewGuid(), "Cut timber", "Use the plan", []);
            using var http = new HttpClient(new BoardHandler(board, entry)) { BaseAddress = new Uri("http://localhost") };
            var view = new TaskBoardWorkspace();
            var controller = new TaskBoardWorkspaceController(new TaskBoardClient(http), view, CancellationToken.None);
            await controller.RefreshAsync();
            await controller.ExecuteAsync("AddTask", entry);

            Assert.Equal((entry.TaskId, "Task added.", true),
                (view.State.Selected?.TaskId, view.State.Message, view.Control<Avalonia.Controls.Button>("AddTask").IsEnabled));
        });
    }

    [Theory]
    [InlineData("Claim", "/claim")]
    [InlineData("Complete", "/complete")]
    [InlineData("Block", "/status")]
    [InlineData("Reopen", "/status")]
    [InlineData("Cancel", "/status")]
    [InlineData("AddCriterion", "/criteria")]
    [InlineData("Criterion", "/criteria/")]
    [InlineData("Work", "/work")]
    [InlineData("Plan", "/plan")]
    public async Task ExecuteAsync_Should_Route_Explicit_Actions_To_The_Existing_Api(string action, string route)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var task = TaskBoardWorkspaceStateTests.Task("Build shelves") with
            {
                AcceptanceCriteria = [new AcceptanceCriterion(Guid.NewGuid(), "Fits", 0, false, null, null)],
            };
            var board = TaskBoardWorkspaceStateTests.Board(task);
            var entry = new TaskBoardEntry(board.BoardId, null, task.TaskId, task.Title, "", []);
            var handler = new BoardHandler(board, entry);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            view.FocusTask(task.TaskId);
            view.Control<Avalonia.Controls.TextBox>("StatusDetail").Text = "Reason";
            view.Control<Avalonia.Controls.TextBox>("NewCriterion").Text = "Measured";
            view.Control<Avalonia.Controls.TextBox>("FeatureRequest").Text = "Build storage";
            var controller = new TaskBoardWorkspaceController(new TaskBoardClient(http), view, CancellationToken.None);
            object target = action == "Criterion" ? view.State.Selected!.Criteria[0] : view.State.Selected!;
            await controller.ExecuteAsync(action, target);

            Assert.Contains(route, handler.LastMutation ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task FailedAdvice_Should_End_Pending_State_And_Keep_The_Selected_Task()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var task = TaskBoardWorkspaceStateTests.Task("Build shelves");
            var board = TaskBoardWorkspaceStateTests.Board(task);
            var entry = new TaskBoardEntry(board.BoardId, null, task.TaskId, task.Title, "", []);
            using var http = new HttpClient(new BoardHandler(board, entry) { FailWrites = true })
            { BaseAddress = new Uri("http://localhost") };
            var view = new TaskBoardWorkspace();
            var controller = new TaskBoardWorkspaceController(new TaskBoardClient(http), view, CancellationToken.None);
            await controller.RefreshAsync();
            var stillLoading = view.State.Message.Contains("loading", StringComparison.OrdinalIgnoreCase);
            view.Present(board);
            view.FocusTask(task.TaskId);
            await controller.ExecuteAsync("Work", view.State.Selected);

            Assert.Equal((false, "Advice failed: offline", task.TaskId),
                (stillLoading, view.Control<Avalonia.Controls.TextBlock>("AdviceStatus").Text, view.State.Selected?.TaskId));
        });
    }

    [Fact]
    public async Task Save_Should_Select_The_New_Task_When_A_Poll_Is_Already_Running()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var board = TaskBoardWorkspaceStateTests.Board();
            var entry = new TaskBoardEntry(board.BoardId, null, Guid.NewGuid(), "Cut timber", "", []);
            var handler = new BoardHandler(board, entry) { DeferList = true };
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var view = new TaskBoardWorkspace();
            view.SelectBoard(board.BoardId);
            view.Present(board);
            var controller = new TaskBoardWorkspaceController(new TaskBoardClient(http), view, CancellationToken.None);
            var poll = controller.RefreshAsync();
            var save = controller.ExecuteAsync("AddTask", entry);
            handler.ResumeList();
            await Task.WhenAll(poll, save);

            Assert.Equal(entry.TaskId, view.State.Selected?.TaskId);
        });
    }

    private sealed class BoardHandler(TaskBoardSnapshot board, TaskBoardEntry entry) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
        private bool added;
        private readonly TaskCompletionSource<HttpResponseMessage> pendingList = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool DeferList { get; set; }

        internal void ResumeList() => this.pendingList.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new TaskBoardSummary(board.BoardId, board.Title, board.Status, default, 0, 0, 0) }, options: options),
        });
        internal string? LastMutation { get; private set; }
        internal bool FailWrites { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/task-boards" && this.DeferList)
            {
                this.DeferList = false;
                return this.pendingList.Task.WaitAsync(cancellationToken);
            }

            if (request.Method != HttpMethod.Get)
            {
                return this.MutateAsync(path);
            }

            if (path.EndsWith("/activity", StringComparison.Ordinal))
            {
                return ReplyAsync(Array.Empty<TaskBoardActivity>());
            }

            if (path == "/task-boards")
            {
                return ReplyAsync(new[] { new TaskBoardSummary(board.BoardId, board.Title, board.Status, default, this.added ? 1 : 0, 0, 0) });
            }

            return ReplyAsync(board with
            {
                Tasks = this.added
                ? [TaskBoardWorkspaceStateTests.Task(entry.Title) with { TaskId = entry.TaskId }] : []
            });
        }

        private Task<HttpResponseMessage> MutateAsync(string path)
        {
            if (this.FailWrites)
            {
                throw new HttpRequestException("offline");
            }

            this.LastMutation = path;
            if (path.EndsWith("/work", StringComparison.Ordinal))
            {
                return ReplyAsync(new { ran = true, traceId = Guid.NewGuid(), answer = "Measure first." });
            }

            if (path == "/task-boards/plan")
            {
                return ReplyAsync(new { boardId = board.BoardId });
            }

            this.added = path.EndsWith("/tasks", StringComparison.Ordinal);
            return ReplyAsync(new { taskId = entry.TaskId, updated = true });
        }

        private static Task<HttpResponseMessage> ReplyAsync(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body, body.GetType(), null, options),
        });
    }
}
