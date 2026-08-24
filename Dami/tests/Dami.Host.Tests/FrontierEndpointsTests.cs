using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

/// <summary>
/// The subscription frontier (ADR-0011) as a turn mode, and the boundary behaviour that
/// once escaped as an unhandled 500 — which made every client report the host as
/// unreachable when the runtime had simply, correctly, refused.
/// </summary>
public sealed class FrontierEndpointsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 11, 0, 0, TimeSpan.Zero);

    private readonly IFrontierChat frontierChat = Substitute.For<IFrontierChat>();
    private readonly ISessionTurnRunner localRunner = Substitute.For<ISessionTurnRunner>();
    private readonly ISessionTurnRunner frontierRunner = Substitute.For<ISessionTurnRunner>();

    [Fact]
    public async Task PostFrontier_Should_Return_Forbidden_When_The_Boundary_Refuses()
    {
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new EgressRefusedException("the frontier is not enabled"));

        using var response = await this.AskFrontierAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostFrontier_Should_Explain_Why_It_Refused()
    {
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new EgressRefusedException("the frontier is not enabled"));

        using var response = await this.AskFrontierAsync();

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Contains(
            "not enabled", body!.RootElement.GetProperty("refused").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostFrontier_Should_Name_The_Cause_When_A_Dependency_Fails()
    {
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException(
                "Connection refused (127.0.0.1:8080)"));

        using var response = await this.AskFrontierAsync();

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Contains(
            "Connection refused", body!.RootElement.GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostTurn_Should_Use_The_Frontier_When_Asked()
    {
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns("an answer from the subscription");

        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/turns", new { message = "hello", frontier = true }, CancellationToken.None);

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("Frontier", body!.RootElement.GetProperty("route").GetString());
    }

    [Fact]
    public async Task PostTurn_Should_Send_No_Retrieved_Memory_To_The_Frontier()
    {
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns("an answer");

        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/turns", new { message = "hello", frontier = true }, CancellationToken.None);

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(0, body!.RootElement.GetProperty("memories").GetInt32());
    }

    [Fact]
    public async Task PostSessionTurn_Should_Route_To_The_Frontier_Runner_When_Flagged()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        this.frontierRunner.RunAsync(Arg.Any<ConversationTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(Outcome(sessionId, requestId));

        await this.RunSessionTurnAsync(sessionId, requestId, frontier: true);

        await this.frontierRunner.Received(1).RunAsync(
            Arg.Any<ConversationTurnRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostSessionTurn_Should_Not_Use_The_Local_Runner_When_Flagged()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        this.frontierRunner.RunAsync(Arg.Any<ConversationTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(Outcome(sessionId, requestId));

        await this.RunSessionTurnAsync(sessionId, requestId, frontier: true);

        await this.localRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task PostSessionTurn_Should_Stay_Local_By_Default()
    {
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        this.localRunner.RunAsync(Arg.Any<ConversationTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(Outcome(sessionId, requestId));

        await this.RunSessionTurnAsync(sessionId, requestId, frontier: false);

        await this.frontierRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    private async Task RunSessionTurnAsync(Guid sessionId, Guid requestId, bool frontier)
    {
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/turns",
            new { requestId, message = "hello", frontier },
            CancellationToken.None);
    }

    private async Task<HttpResponseMessage> AskFrontierAsync()
    {
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(
            "/frontier", new { question = "anything" }, CancellationToken.None);
    }

    private static SessionTurnOutcome Outcome(Guid sessionId, Guid requestId)
    {
        return new SessionTurnOutcome(
            new ConversationTurn(
                1,
                new ConversationTurnRequest(sessionId, requestId, "hello", at),
                Guid.NewGuid(),
                ConversationTurnState.Completed,
                "an answer",
                at.AddSeconds(3)),
            wasReplay: false);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFrontierChat>();
                services.RemoveAll<ISessionTurnRunner>();
                services.AddSingleton(this.frontierChat);
                services.AddSingleton(this.localRunner);
                services.AddKeyedSingleton("frontier", (_, _) => this.frontierRunner);
            }));
    }
}
