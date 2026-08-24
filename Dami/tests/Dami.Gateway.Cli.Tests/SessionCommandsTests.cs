using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class SessionCommandsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_Should_Post_A_Stable_Id_And_Print_The_Created_Session()
    {
        Guid sentId = default;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/sessions", request.RequestUri!.AbsolutePath);
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
            sentId = body!.RootElement.GetProperty("sessionId").GetGuid();
            return Json(HttpStatusCode.Created, new
            {
                sessionId = sentId,
                state = "Active",
                createdAt = at,
                updatedAt = at,
            });
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(
            () => commands.StartAsync(null, CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.NotEqual(Guid.Empty, sentId);
        Assert.Contains(sentId.ToString("D"), output, StringComparison.Ordinal);
        Assert.Contains("Active", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_Should_Get_And_Print_Recent_Sessions()
    {
        var sessionId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/sessions", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, new[]
            {
                new
                {
                    sessionId,
                    state = "Interrupted",
                    createdAt = at,
                    updatedAt = at,
                },
            }));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(
            () => commands.ListAsync(CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains(sessionId.ToString("D"), output, StringComparison.Ordinal);
        Assert.Contains("Interrupted", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumeAsync_Should_Post_And_Print_The_Active_State()
    {
        var sessionId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/sessions/{sessionId:D}/resume", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, new
            {
                sessionId,
                state = "Active",
                createdAt = at,
                updatedAt = at,
            }));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(
            () => commands.ResumeAsync(sessionId.ToString("D"), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Active", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterruptAsync_Should_Post_And_Print_The_Interrupted_State()
    {
        var sessionId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/sessions/{sessionId:D}/interrupt", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, new
            {
                sessionId,
                state = "Interrupted",
                createdAt = at,
                updatedAt = at,
            }));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(
            () => commands.InterruptAsync(sessionId.ToString("D"), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Interrupted", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnAsync_Should_Print_The_Request_Id_Before_Posting_And_Then_The_Response()
    {
        var sessionId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        Guid requestId = default;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal($"/sessions/{sessionId:D}/turns", request.RequestUri!.AbsolutePath);
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
            requestId = body!.RootElement.GetProperty("requestId").GetGuid();
            return TurnResponse(sessionId, requestId, traceId);
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(
            () => commands.TurnAsync(sessionId.ToString("D"), "question", CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.NotEqual(Guid.Empty, requestId);
        Assert.StartsWith($"request {requestId:D}", output, StringComparison.Ordinal);
        Assert.Contains("Dami: answer", output, StringComparison.Ordinal);
        Assert.Contains(traceId.ToString("N")[..8], output, StringComparison.Ordinal);
        Assert.Contains("reconnect", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconnectAsync_Should_Get_And_Print_The_Durable_Turn()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                $"/sessions/{sessionId:D}/turns/{requestId:D}",
                request.RequestUri!.AbsolutePath);
            return Task.FromResult(StoredTurnResponse(sessionId, requestId, traceId));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, output) = await CaptureAsync(() => commands.ReconnectAsync(
            sessionId.ToString("D"), requestId.ToString("D"), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Completed", output, StringComparison.Ordinal);
        Assert.Contains("Dami: answer", output, StringComparison.Ordinal);
        Assert.Contains(traceId.ToString("N"), output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCommandRouter_Should_Dispatch_The_Sessions_Verb()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/sessions", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, Array.Empty<object>()));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, _) = await CaptureAsync(() => SessionCommandRouter.RunAsync(
            ["sessions"], commands, CancellationToken.None));

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task StartAsync_Should_Reject_The_Empty_Guid_Without_An_Http_Call()
    {
        var called = false;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(Json(HttpStatusCode.BadRequest, new { }));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));

        var (exitCode, _) = await CaptureAsync(
            () => commands.StartAsync(Guid.Empty.ToString("D"), CancellationToken.None));

        Assert.Equal(2, exitCode);
        Assert.False(called);
    }

    [Fact]
    public async Task SessionOperations_Should_Reject_Empty_Guids_Without_Http_Calls()
    {
        var called = false;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(Json(HttpStatusCode.BadRequest, new { }));
        }));
        var commands = new SessionCommands(new DamiApiClient(http));
        var empty = Guid.Empty.ToString("D");

        var resume = await commands.ResumeAsync(empty, CancellationToken.None);
        var turn = await commands.TurnAsync(empty, "question", CancellationToken.None);
        var reconnect = await commands.ReconnectAsync(
            Guid.NewGuid().ToString("D"), empty, CancellationToken.None);

        Assert.Equal([2, 2, 2], [resume, turn, reconnect]);
        Assert.False(called);
    }

    private static HttpResponseMessage TurnResponse(
        Guid sessionId,
        Guid requestId,
        Guid traceId)
    {
        return Json(HttpStatusCode.OK, new
        {
            turn = new
            {
                sequence = 1,
                request = new { sessionId, requestId, message = "question", requestedAt = at },
                traceId,
                state = "Completed",
                response = "answer",
                completedAt = at,
            },
            wasReplay = false,
        });
    }

    private static HttpResponseMessage StoredTurnResponse(
        Guid sessionId,
        Guid requestId,
        Guid traceId)
    {
        return Json(HttpStatusCode.OK, new
        {
            sequence = 1,
            request = new { sessionId, requestId, message = "question", requestedAt = at },
            traceId,
            state = "Completed",
            response = "answer",
            completedAt = at,
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> command)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            return (await command(), output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection
{
}
