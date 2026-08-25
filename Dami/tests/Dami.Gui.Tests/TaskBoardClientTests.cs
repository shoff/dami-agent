using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.TaskBoard;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TaskBoardClientTests
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task FindAsync_Should_Read_The_Shared_Recursive_Contract()
    {
        var boardId = Guid.NewGuid();
        var child = new BoardTask(
            Guid.NewGuid(), "child", "nested", TaskBoardStatus.Open,
            TaskPriority.High, 0, TaskOrdering.Ordered, null, 1, [], [], []);
        var root = new BoardTask(
            Guid.NewGuid(), "root", "recursive", TaskBoardStatus.InProgress,
            TaskPriority.Critical, 0, TaskOrdering.Ordered,
            new TaskClaim(new TaskActor("codex", TaskActorKind.Agent),
                new DateTimeOffset(2026, 8, 24, 23, 20, 0, TimeSpan.Zero)),
            2, [], [], [child]);
        var snapshot = new TaskBoardSnapshot(
            boardId, "Dami", "request", "plan",
            new TaskActor("steve", TaskActorKind.Human),
            new DateTimeOffset(2026, 8, 24, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 24, 23, 20, 0, TimeSpan.Zero),
            TaskBoardStatus.InProgress, TaskOrdering.Ordered, [root]);
        var handler = new RecordingHandler(snapshot);
        var client = new TaskBoardClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5810"),
        });

        var found = await client.FindAsync(boardId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal((snapshot.BoardId, snapshot.Title, snapshot.Status),
            (found.BoardId, found.Title, found.Status));
        Assert.Equal(root.TaskId, Assert.Single(found.Tasks).TaskId);
        Assert.Equal(child.TaskId, Assert.Single(found.Tasks[0].SubTasks).TaskId);
        Assert.Equal($"/task-boards/{boardId:D}", handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task ListAsync_Should_Read_Bounded_Progress_Summaries()
    {
        var summary = new TaskBoardSummary(
            Guid.NewGuid(), "Dami", TaskBoardStatus.Blocked,
            new DateTimeOffset(2026, 8, 24, 23, 30, 0, TimeSpan.Zero), 12, 7, 2);
        var handler = new RecordingHandler(new[] { summary });
        var client = new TaskBoardClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5810"),
        });

        var boards = await client.ListAsync(20, CancellationToken.None);

        var found = Assert.Single(boards);
        Assert.Equal((summary.BoardId, 12, 7, 2),
            (found.BoardId, found.TotalTasks, found.DoneTasks, found.BlockedTasks));
        Assert.Equal("/task-boards?limit=20", handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task ClaimAsync_Should_Report_An_Optimistic_Conflict()
    {
        var taskId = Guid.NewGuid();
        var handler = new RecordingHandler(
            new { updated = false }, HttpStatusCode.Conflict);
        var client = new TaskBoardClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5810"),
        });

        var outcome = await client.ClaimAsync(
            taskId, 7, new TaskActor("codex", TaskActorKind.Agent), CancellationToken.None);

        Assert.Equal(TaskBoardMutationOutcome.Conflict, outcome);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/task-boards/tasks/{taskId:D}/claim", handler.RequestUri?.PathAndQuery);
        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        Assert.Equal(7, body.RootElement.GetProperty("expectedVersion").GetInt64());
        Assert.Equal("codex", body.RootElement.GetProperty("actorId").GetString());
        Assert.Equal("Agent", body.RootElement.GetProperty("actorKind").GetString());
    }

    [Fact]
    public async Task Remaining_Mutations_Should_Map_To_The_Runtime_Contracts()
    {
        var actor = new TaskActor("steve", TaskActorKind.Human);
        var taskId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var criterion = CreateClient(new { updated = true });
        var complete = CreateClient(new { updated = true });
        var status = CreateClient(new { updated = true });

        Assert.Equal(TaskBoardMutationOutcome.Updated,
            await criterion.Client.SetCriterionAsync(
                criterionId, 3, true, actor, CancellationToken.None));
        Assert.Equal(TaskBoardMutationOutcome.Updated,
            await complete.Client.CompleteAsync(taskId, 4, actor, CancellationToken.None));
        Assert.Equal(TaskBoardMutationOutcome.Updated,
            await status.Client.SetStatusAsync(
                taskId, 5, TaskBoardStatus.Blocked, "waiting", actor,
                CancellationToken.None));

        Assert.Equal((HttpMethod.Put, $"/task-boards/criteria/{criterionId:D}"),
            (criterion.Handler.Method, criterion.Handler.RequestUri?.PathAndQuery));
        Assert.Equal((HttpMethod.Post, $"/task-boards/tasks/{taskId:D}/complete"),
            (complete.Handler.Method, complete.Handler.RequestUri?.PathAndQuery));
        Assert.Equal((HttpMethod.Put, $"/task-boards/tasks/{taskId:D}/status"),
            (status.Handler.Method, status.Handler.RequestUri?.PathAndQuery));
    }

    [Fact]
    public async Task Activity_And_Planning_Should_Map_To_The_Runtime_Contracts()
    {
        var actor = new TaskActor("steve", TaskActorKind.Human);
        var boardId = Guid.NewGuid();
        var activity = new TaskBoardActivity(
            8, Guid.NewGuid(), boardId, null, null, TaskBoardActivityKind.BoardCreated,
            actor, new DateTimeOffset(2026, 8, 24, 23, 40, 0, TimeSpan.Zero),
            null, null, null);
        var activityClient = CreateClient(new[] { activity });
        var planning = CreateClient(new { boardId });

        Assert.Equal(activity, Assert.Single(await activityClient.Client.ActivityAsync(
            boardId, 50, CancellationToken.None)));
        Assert.Equal(boardId, await planning.Client.PlanAsync(
            boardId, "Build it", actor, FeaturePlannerKind.Dami,
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Equal($"/task-boards/{boardId:D}/activity?limit=50",
            activityClient.Handler.RequestUri?.PathAndQuery);
        Assert.Equal("/task-boards/plan", planning.Handler.RequestUri?.PathAndQuery);
    }

    private static (TaskBoardClient Client, RecordingHandler Handler) CreateClient(object payload)
    {
        var handler = new RecordingHandler(payload);
        return (new TaskBoardClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5810"),
        }), handler);
    }

    private sealed class RecordingHandler(
        object payload,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        internal HttpMethod? Method { get; private set; }

        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestUri = request.RequestUri;
            this.Method = request.Method;
            this.Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(payload, payload.GetType(), null, jsonOptions),
            };
        }
    }
}
