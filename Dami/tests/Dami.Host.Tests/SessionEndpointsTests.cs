using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class SessionEndpointsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 11, 0, 0, TimeSpan.Zero);

    private readonly IConversationSessionManager manager =
        Substitute.For<IConversationSessionManager>();
    private readonly ISessionTurnRunner turnRunner = Substitute.For<ISessionTurnRunner>();
    private readonly IConversationTurnStore turnStore = Substitute.For<IConversationTurnStore>();

    [Fact]
    public async Task PostSessions_Should_Start_The_ClientIdentified_Session()
    {
        var sessionId = Guid.NewGuid();
        var session = new ConversationSession(
            sessionId, ConversationSessionState.Active, at, at);
        this.manager.StartAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sessions", new { sessionId }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.Equal(sessionId, body.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal("Active", body.RootElement.GetProperty("state").GetString());
        Assert.Equal($"/sessions/{sessionId:D}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetSessions_Should_List_The_Most_Recently_Active_Sessions()
    {
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Interrupted, at, at.AddMinutes(1));
        this.manager.ListRecentAsync(20, Arg.Any<CancellationToken>())
            .Returns(SessionsAsync(session));
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sessions", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var item = Assert.Single(body!.RootElement.EnumerateArray());
        Assert.Equal(session.SessionId, item.GetProperty("sessionId").GetGuid());
        Assert.Equal("Interrupted", item.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GetSession_Should_Return_The_Durable_Current_State()
    {
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, at, at);
        this.manager.FindAsync(session.SessionId, Arg.Any<CancellationToken>()).Returns(session);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/sessions/{session.SessionId:D}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(session.SessionId, body!.RootElement.GetProperty("sessionId").GetGuid());
    }

    [Fact]
    public async Task PostResume_Should_Return_The_Active_Durable_State()
    {
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, at.AddMinutes(-1), at);
        this.manager.ResumeAsync(session.SessionId, Arg.Any<CancellationToken>()).Returns(session);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/sessions/{session.SessionId:D}/resume", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("Active", body!.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task PostInterrupt_Should_Return_The_Interrupted_Durable_State()
    {
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Interrupted, at.AddMinutes(-1), at);
        this.manager.InterruptAsync(session.SessionId, Arg.Any<CancellationToken>()).Returns(session);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/sessions/{session.SessionId:D}/interrupt", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("Interrupted", body!.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task PostSessionTurn_Should_Execute_With_The_Client_Request_Id()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new ConversationTurnRequest(sessionId, requestId, "question", at);
        var completed = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Completed, "answer", at);
        this.turnRunner.RunAsync(
                Arg.Is<ConversationTurnRequest>(item =>
                    item.SessionId == sessionId
                    && item.RequestId == requestId
                    && item.Message == "question"),
                Arg.Any<CancellationToken>())
            .Returns(new SessionTurnOutcome(completed, false));
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/turns",
            new { requestId, message = "question" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.False(body!.RootElement.GetProperty("wasReplay").GetBoolean());
        Assert.Equal("answer", body.RootElement.GetProperty("turn")
            .GetProperty("response").GetString());
    }

    [Fact]
    public async Task GetSessionTurn_Should_Reconnect_To_The_Durable_Request_State()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new ConversationTurnRequest(sessionId, requestId, "question", at);
        var running = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Running);
        this.turnStore.FindTurnAsync(sessionId, requestId, Arg.Any<CancellationToken>())
            .Returns(running);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/sessions/{sessionId:D}/turns/{requestId:D}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(running.TraceId, body!.RootElement.GetProperty("traceId").GetGuid());
        Assert.Equal("Running", body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task PostSessions_Should_Reject_An_Empty_Stable_Id()
    {
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sessions", new { sessionId = Guid.Empty }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await this.manager.DidNotReceiveWithAnyArgs().StartAsync(default, default);
    }

    [Theory]
    [InlineData(true, "question")]
    [InlineData(false, "   ")]
    public async Task PostSessionTurn_Should_Reject_An_Invalid_Request(
        bool emptyRequestId,
        string message)
    {
        var sessionId = Guid.NewGuid();
        var requestId = emptyRequestId ? Guid.Empty : Guid.NewGuid();
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/turns",
            new { requestId, message }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await this.turnRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task PostSessionTurn_Should_Return_Durable_Interruption_When_The_Session_Cancels_It()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new ConversationTurnRequest(sessionId, requestId, "question", at);
        var interrupted = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Interrupted, completedAt: at);
        this.turnRunner.RunAsync(
                Arg.Any<ConversationTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<SessionTurnOutcome>>(_ => throw new OperationCanceledException());
        this.turnStore.FindTurnAsync(sessionId, requestId, CancellationToken.None)
            .Returns(interrupted);
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/turns",
            new { requestId, message = "question" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("Interrupted", body!.RootElement.GetProperty("turn")
            .GetProperty("state").GetString());
    }

    private static async IAsyncEnumerable<ConversationSession> SessionsAsync(
        params ConversationSession[] sessions)
    {
        foreach (var session in sessions)
        {
            yield return session;
        }

        await Task.CompletedTask;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IConversationSessionManager>();
                services.RemoveAll<ISessionTurnRunner>();
                services.RemoveAll<IConversationTurnStore>();
                services.AddSingleton(this.manager);
                services.AddSingleton(this.turnRunner);
                services.AddSingleton(this.turnStore);
            }));
    }
}
