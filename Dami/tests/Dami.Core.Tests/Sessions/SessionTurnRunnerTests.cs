using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Sessions;

public sealed class SessionTurnRunnerTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);

    private readonly IConversationTurnStore turnStore = Substitute.For<IConversationTurnStore>();
    private readonly IConversationWindowBuilder windowBuilder =
        Substitute.For<IConversationWindowBuilder>();
    private readonly ITracedTurnRunner tracedTurnRunner = Substitute.For<ITracedTurnRunner>();
    private readonly SessionCancellationRegistry cancellationRegistry = new();

    [Fact]
    public async Task RunAsync_Should_Reserve_Execute_And_Complete_One_Turn()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        var completed = Turn(request, ConversationTurnState.Completed, "answer");
        var window = new ConversationWindow([], 0);
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>()).Returns(window);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, window, Arg.Any<CancellationToken>())
            .Returns(Result(running.TraceId, "answer"));
        this.turnStore.CompleteTurnAsync(
                request.SessionId, request.RequestId, "answer", at, Arg.Any<CancellationToken>())
            .Returns(true);
        this.turnStore.FindTurnAsync(
            request.SessionId, request.RequestId, Arg.Any<CancellationToken>()).Returns(completed);

        var outcome = await this.CreateRunner().RunAsync(request, CancellationToken.None);

        Assert.False(outcome.WasReplay);
        Assert.Equal(completed, outcome.Turn);
        await this.tracedTurnRunner.Received(1).RunTracedAsync(
            running.TraceId, request.Message, window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Return_An_Existing_Request_Without_Reexecution()
    {
        var request = Request();
        var completed = Turn(request, ConversationTurnState.Completed, "stored answer");
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(completed, false));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                completed.TraceId, request.Message, Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns(Result(completed.TraceId, "duplicate answer"));
        this.turnStore.FindTurnAsync(
            request.SessionId, request.RequestId, Arg.Any<CancellationToken>()).Returns(completed);

        var outcome = await this.CreateRunner().RunAsync(request, CancellationToken.None);

        Assert.True(outcome.WasReplay);
        Assert.Equal("stored answer", outcome.Turn.Response);
        await this.tracedTurnRunner.DidNotReceiveWithAnyArgs().RunTracedAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task RunAsync_Should_Mark_The_Reserved_Turn_Failed_And_Rethrow()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, ConversationWindow.Empty, Arg.Any<CancellationToken>())
            .Returns<Task<TurnResult>>(_ => throw new InvalidOperationException("model failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateRunner().RunAsync(request, CancellationToken.None));

        await this.turnStore.Received(1).FailTurnAsync(
            request.SessionId, request.RequestId, at, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_Should_Mark_The_Reserved_Turn_Interrupted_On_Cancellation()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        using var cancellation = new CancellationTokenSource();
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, ConversationWindow.Empty, Arg.Any<CancellationToken>())
            .Returns(_ => CancelAsync(cancellation));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateRunner().RunAsync(request, cancellation.Token));

        await this.turnStore.Received(1).InterruptTurnAsync(
            request.SessionId, request.RequestId, at, CancellationToken.None);
        await this.turnStore.DidNotReceiveWithAnyArgs().FailTurnAsync(
            default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_Should_Durably_Complete_A_Response_When_Cancellation_Arrives_After_Execution()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        var completed = Turn(request, ConversationTurnState.Completed, "answer");
        using var cancellation = new CancellationTokenSource();
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, ConversationWindow.Empty, Arg.Any<CancellationToken>())
            .Returns(_ => CompleteThenCancelAsync(cancellation, running.TraceId));
        this.turnStore.CompleteTurnAsync(
                request.SessionId, request.RequestId, "answer", at, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<CancellationToken>(4).ThrowIfCancellationRequested();
                return true;
            });
        this.turnStore.FindTurnAsync(
            request.SessionId, request.RequestId, CancellationToken.None).Returns(completed);

        var outcome = await this.CreateRunner().RunAsync(request, cancellation.Token);

        Assert.Equal(completed, outcome.Turn);
        await this.turnStore.Received(1).CompleteTurnAsync(
            request.SessionId, request.RequestId, "answer", at, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_Should_Return_The_Stored_Terminal_State_When_Completion_Loses_A_Race()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        var interrupted = Turn(request, ConversationTurnState.Interrupted);
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, ConversationWindow.Empty, Arg.Any<CancellationToken>())
            .Returns(Result(running.TraceId, "answer"));
        this.turnStore.CompleteTurnAsync(
                request.SessionId, request.RequestId, "answer", at, Arg.Any<CancellationToken>())
            .Returns(false);
        this.turnStore.FindTurnAsync(
            request.SessionId, request.RequestId, Arg.Any<CancellationToken>()).Returns(interrupted);

        var outcome = await this.CreateRunner().RunAsync(request, CancellationToken.None);

        Assert.Equal(ConversationTurnState.Interrupted, outcome.Turn.State);
        Assert.Null(outcome.Turn.Response);
    }

    [Fact]
    public async Task RunAsync_Should_Cancel_Active_Execution_When_The_Session_Is_Interrupted()
    {
        var request = Request();
        var running = Turn(request, ConversationTurnState.Running);
        var executionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        this.turnStore.ReserveTurnAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ConversationTurnReservation(running, true));
        this.windowBuilder.BuildAsync(request.SessionId, Arg.Any<CancellationToken>())
            .Returns(ConversationWindow.Empty);
        this.tracedTurnRunner.RunTracedAsync(
                running.TraceId, request.Message, ConversationWindow.Empty, Arg.Any<CancellationToken>())
            .Returns(call => WaitForSessionCancellationAsync(
                call.ArgAt<CancellationToken>(3), executionStarted));

        var interruption = Task.Run(async () =>
        {
            await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await this.cancellationRegistry.InterruptAsync(request.SessionId, CancellationToken.None).ConfigureAwait(false);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.CreateRunner().RunAsync(request, CancellationToken.None));
        await interruption;
        await this.turnStore.Received(1).InterruptTurnAsync(
            request.SessionId, request.RequestId, at, CancellationToken.None);
    }

    private static async Task<TurnResult> CancelAsync(CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
        throw new OperationCanceledException(cancellation.Token);
    }

    private static async Task<TurnResult> CompleteThenCancelAsync(
        CancellationTokenSource cancellation,
        Guid traceId)
    {
        await cancellation.CancelAsync();
        return Result(traceId, "answer");
    }

    private static async Task<TurnResult> WaitForSessionCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource executionStarted)
    {
        executionStarted.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("An infinite delay cannot complete.");
    }

    private static ConversationTurnRequest Request()
    {
        return new ConversationTurnRequest(Guid.NewGuid(), Guid.NewGuid(), "question", at);
    }

    private static ConversationTurn Turn(
        ConversationTurnRequest request,
        ConversationTurnState state,
        string? response = null)
    {
        return new ConversationTurn(
            1, request, Guid.NewGuid(), state, response,
            state == ConversationTurnState.Running ? null : at);
    }

    private static TurnResult Result(Guid traceId, string answer)
    {
        return new TurnResult(
            traceId, answer, new AssembledContext([], [], 0),
            new ModelRoute(ModelTier.Local, PrivacyClass.LocalOnly, "test"));
    }

    private SessionTurnRunner CreateRunner()
    {
        return new SessionTurnRunner(
            this.turnStore, this.windowBuilder, this.tracedTurnRunner,
            this.cancellationRegistry, new FakeTimeProvider(at));
    }
}
