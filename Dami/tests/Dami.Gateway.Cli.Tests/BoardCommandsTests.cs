using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class BoardCommandsTests
{
    private static readonly Guid boardId = Guid.Parse("d621fe5f-a42b-e454-82d4-954ab40c99f3");
    private static readonly Guid epicId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid openId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid doneId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    private static readonly TaskActor claude = new("claude", TaskActorKind.Agent);

    [Fact]
    public async Task ListAsync_Should_Print_Each_Board_With_Progress()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/task-boards", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, new[] { Summary() }));
        }));

        var (exitCode, output) = await CaptureAsync(
            () => new BoardCommands(new DamiApiClient(http), claude).ListAsync(CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("d621fe5f", output, StringComparison.Ordinal);
        Assert.Contains("153/204", output, StringComparison.Ordinal);
        Assert.Contains("Dami Core suite", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowAsync_Should_Render_The_Tree_And_Hide_Finished_Work_When_Asked()
    {
        using var http = new HttpClient(new StubHandler(ReadOnlyBoardAsync));
        var commands = new BoardCommands(new DamiApiClient(http), claude);

        var (_, everything) = await CaptureAsync(
            () => commands.ShowAsync("dami", openOnly: false, CancellationToken.None));
        var (exitCode, openOnly) = await CaptureAsync(
            () => commands.ShowAsync("d621", openOnly: true, CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("cccccccc  [x]", everything, StringComparison.Ordinal);
        Assert.Contains("bbbbbbbb  [ ]   Open child", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("cccccccc", openOnly, StringComparison.Ordinal);
        Assert.Contains("bbbbbbbb", openOnly, StringComparison.Ordinal);
        Assert.Contains("@codex", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimAsync_Should_Resolve_The_Prefix_And_Post_The_Read_Version_As_The_Actor()
    {
        JsonElement sent = default;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/task-boards/tasks/{openId:D}/claim", request.RequestUri!.AbsolutePath);
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
                sent = body!.RootElement.Clone();
                return Json(HttpStatusCode.OK, new { updated = true });
            }

            return await ReadOnlyBoardAsync(request);
        }));

        var (exitCode, output) = await CaptureAsync(() => new BoardCommands(new DamiApiClient(http), claude)
            .ClaimAsync("bbbbbbbb", "taking this", CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Equal(3, sent.GetProperty("expectedVersion").GetInt64());
        Assert.Equal("claude", sent.GetProperty("actorId").GetString());
        Assert.Equal("Agent", sent.GetProperty("actorKind").GetString());
        Assert.Equal("taking this", sent.GetProperty("detail").GetString());
        Assert.Contains("claim: Open child", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_Should_Report_A_Conflict_Without_Retrying()
    {
        var posts = 0;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                return Task.FromResult(Json(HttpStatusCode.Conflict, new { updated = false }));
            }

            return ReadOnlyBoardAsync(request);
        }));

        var (exitCode, output) = await CaptureAsync(() => new BoardCommands(new DamiApiClient(http), claude)
            .CompleteAsync("bbbbbbbb", null, CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Equal(1, posts);
        Assert.Contains("conflict", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimAsync_Should_Refuse_An_Ambiguous_Or_Unknown_Prefix_Without_Mutating()
    {
        var posts = 0;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            return ReadOnlyBoardAsync(request);
        }));
        var commands = new BoardCommands(new DamiApiClient(http), claude);

        var (unknown, _) = await CaptureAsync(() => commands.ClaimAsync("ffffffff", null, CancellationToken.None));
        // "aaaaaaaa", "bbbbbbbb" and "cccccccc" are distinct; a single hex digit is not a task.
        var (ambiguous, _) = await CaptureAsync(() => commands.ClaimAsync("", null, CancellationToken.None));

        Assert.Equal(1, unknown);
        Assert.Equal(1, ambiguous);
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task AddAsync_Should_Post_Under_The_Parent_At_The_Next_Position_With_Criteria()
    {
        JsonElement sent = default;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/task-boards/{boardId:D}/tasks", request.RequestUri!.AbsolutePath);
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
                sent = body!.RootElement.Clone();
                return Json(HttpStatusCode.Created, new { taskId = Guid.NewGuid() });
            }

            return await ReadOnlyBoardAsync(request);
        }));

        var (exitCode, output) = await CaptureAsync(() => new BoardCommands(new DamiApiClient(http), claude)
            .AddAsync("aaaaaaaa", "A9 Write the thing", ["it exists"], CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Equal(epicId, sent.GetProperty("parentTaskId").GetGuid());
        Assert.Equal(BoardImportIds.Task(TodoBoardMapper.BOARD_KEY, "A9"), sent.GetProperty("taskId").GetGuid());
        Assert.Equal(2, sent.GetProperty("position").GetInt32());
        Assert.Equal("it exists", sent.GetProperty("criteria")[0].GetString());
        Assert.Equal("claude", sent.GetProperty("actorId").GetString());
        Assert.Contains("added", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAsync_Should_Name_An_Older_Runtime_When_The_Endpoint_Is_Missing()
    {
        using var http = new HttpClient(new StubHandler(request => request.Method == HttpMethod.Post
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : ReadOnlyBoardAsync(request)));

        var (exitCode, output) = await CaptureAsync(() => new BoardCommands(new DamiApiClient(http), claude)
            .AddAsync("aaaaaaaa", "A9 Write the thing", [], CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Contains("older", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAsync_Should_Refuse_A_Title_Without_An_Id_Before_Any_Request()
    {
        var requests = 0;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests++;
            return ReadOnlyBoardAsync(request);
        }));

        var (exitCode, output) = await CaptureAsync(() => new BoardCommands(new DamiApiClient(http), claude)
            .AddAsync("aaaaaaaa", "Write the thing", [], CancellationToken.None));

        Assert.Equal(2, exitCode);
        Assert.Equal(0, requests);
        Assert.Contains("starts with its id", output, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> ReadOnlyBoardAsync(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(request.RequestUri!.AbsolutePath == "/task-boards"
            ? Json(HttpStatusCode.OK, new[] { Summary() })
            : Json(HttpStatusCode.OK, Snapshot()));
    }

    private static object Summary()
    {
        return new
        {
            boardId,
            title = "Dami Core suite",
            status = "InProgress",
            updatedAt = DateTimeOffset.UnixEpoch,
            totalTasks = 204,
            doneTasks = 153,
            blockedTasks = 16,
        };
    }

    private static object Snapshot()
    {
        return new
        {
            boardId,
            title = "Dami Core suite",
            status = "InProgress",
            tasks = new[]
            {
                Node(epicId, "Epic", "InProgress", 2, "codex",
                    Node(openId, "Open child", "Open", 3, null),
                    Node(doneId, "Done child", "Done", 4, null)),
            },
        };
    }

    private static object Node(Guid id, string title, string status, long version, string? holder, params object[] children)
    {
        return new
        {
            taskId = id,
            title,
            status,
            version,
            claim = holder is null ? null : new { actor = new { actorId = holder, kind = "Agent" } },
            acceptanceCriteria = Array.Empty<object>(),
            subTasks = children,
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var original = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            return (await run(), writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
