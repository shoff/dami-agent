using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>The privacy rule: a frontier turn carries only what already went out (D-012).</summary>
public sealed class FrontierTracedTurnRunnerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 24, 22, 0, 0, TimeSpan.Zero);
    private static readonly Guid localTrace = Guid.NewGuid();
    private static readonly Guid frontierTrace = Guid.NewGuid();

    private readonly IFrontierChat frontierChat = Substitute.For<IFrontierChat>();
    private readonly IIdentityProvider identityProvider = Substitute.For<IIdentityProvider>();
    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    public FrontierTracedTurnRunnerTests()
    {
        this.identityProvider.FrontierVoice.Returns("You are Dami.");
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns("an answer");
        // A local turn's trace has no egress; a frontier turn's trace has one.
        this.eventStore.ReplayAsync(localTrace, Arg.Any<CancellationToken>())
            .Returns(EventsAsync(ExecutionEventType.TraceCompleted));
        this.eventStore.ReplayAsync(frontierTrace, Arg.Any<CancellationToken>())
            .Returns(EventsAsync(ExecutionEventType.EgressCompleted));
    }

    [Fact]
    public async Task RunTracedAsync_Should_Withhold_A_Local_Answer_From_The_Frontier()
    {
        var window = new ConversationWindow(
            [Turn(1, localTrace, "what is my health situation", "You have severe aortic stenosis.")], 40);

        var prompt = await this.CapturePromptAsync(window);

        Assert.DoesNotContain("aortic stenosis", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Carry_A_Prior_Frontier_Exchange()
    {
        var window = new ConversationWindow(
            [Turn(1, frontierTrace, "explain HNSW", "Hierarchical navigable small world graphs.")], 40);

        var prompt = await this.CapturePromptAsync(window);

        Assert.Contains("Hierarchical navigable small world", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Always_Carry_The_New_Message()
    {
        var prompt = await this.CapturePromptAsync(ConversationWindow.Empty, "the new question");

        Assert.Contains("the new question", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Send_As_Egressable()
    {
        FrontierPrompt? sent = null;
        this.frontierChat.CompleteAsync(
            Arg.Do<FrontierPrompt>(p => sent = p), Arg.Any<CancellationToken>())
            .Returns("an answer");

        await this.CreateRunner().RunTracedAsync(
            Guid.NewGuid(), "hello", ConversationWindow.Empty, CancellationToken.None);

        Assert.Equal(PrivacyClass.Egressable, sent!.Privacy);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Report_No_Memory_In_Its_Context()
    {
        var result = await this.CreateRunner().RunTracedAsync(
            Guid.NewGuid(), "hello", ConversationWindow.Empty, CancellationToken.None);

        Assert.Empty(result.Context.Memories);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Route_As_Frontier()
    {
        var result = await this.CreateRunner().RunTracedAsync(
            Guid.NewGuid(), "hello", ConversationWindow.Empty, CancellationToken.None);

        Assert.Equal(ModelTier.Frontier, result.Route.Tier);
    }

    private async Task<string> CapturePromptAsync(
        ConversationWindow window,
        string message = "and now this")
    {
        FrontierPrompt? sent = null;
        this.frontierChat.CompleteAsync(
            Arg.Do<FrontierPrompt>(p => sent = p), Arg.Any<CancellationToken>())
            .Returns("an answer");

        await this.CreateRunner().RunTracedAsync(
            Guid.NewGuid(), message, window, CancellationToken.None);

        return sent!.Prompt;
    }

    private static ConversationTurn Turn(long sequence, Guid traceId, string message, string response)
    {
        return new ConversationTurn(
            sequence,
            new ConversationTurnRequest(Guid.NewGuid(), Guid.NewGuid(), message, now),
            traceId,
            ConversationTurnState.Completed,
            response,
            now.AddSeconds(5));
    }

    private static async IAsyncEnumerable<ExecutionEvent> EventsAsync(ExecutionEventType type)
    {
        yield return new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.UserTurn,
            "test", type, ExecutionStatus.Succeeded, now, "event");
        await Task.CompletedTask;
    }

    private FrontierTracedTurnRunner CreateRunner()
    {
        return new FrontierTracedTurnRunner(
            this.frontierChat, this.identityProvider, this.eventStore,
            new FakeTimeProvider(now), NullLogger<FrontierTracedTurnRunner>.Instance);
    }
}
