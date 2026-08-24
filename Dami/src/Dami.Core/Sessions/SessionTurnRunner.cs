using Dami.Contracts.Sessions;
using Dami.Core.Turns;

namespace Dami.Core.Sessions;

/// <summary>Coordinates one idempotent durable turn without owning storage or model details.</summary>
public sealed class SessionTurnRunner
{
    private readonly TimeProvider clock;
    private readonly ITracedTurnRunner tracedTurnRunner;
    private readonly IConversationTurnStore turnStore;
    private readonly IConversationWindowBuilder windowBuilder;

    /// <summary>Creates the runner.</summary>
    public SessionTurnRunner(
        IConversationTurnStore turnStore,
        IConversationWindowBuilder windowBuilder,
        ITracedTurnRunner tracedTurnRunner,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(windowBuilder);
        ArgumentNullException.ThrowIfNull(tracedTurnRunner);
        ArgumentNullException.ThrowIfNull(clock);
        this.turnStore = turnStore;
        this.windowBuilder = windowBuilder;
        this.tracedTurnRunner = tracedTurnRunner;
        this.clock = clock;
    }

    /// <summary>Reserves, executes, and completes one session request.</summary>
    public async Task<SessionTurnOutcome> RunAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reservation = await this.turnStore
            .ReserveTurnAsync(request, cancellationToken).ConfigureAwait(false);
        if (!reservation.IsNew)
        {
            return new SessionTurnOutcome(reservation.Turn, wasReplay: true);
        }

        var result = await this.ExecuteReservedAsync(
            request, reservation.Turn.TraceId, cancellationToken).ConfigureAwait(false);

        // Once execution has produced a response, persist its terminal state even if
        // the caller disconnects between model completion and the database write.
        // Otherwise a successful request can remain Running forever and reconnects
        // can neither replay it nor safely execute it again.
        await this.turnStore.CompleteTurnAsync(
            request.SessionId, request.RequestId, result.Answer,
            this.clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        var stored = await this.turnStore.FindTurnAsync(
            request.SessionId, request.RequestId, CancellationToken.None).ConfigureAwait(false);
        return new SessionTurnOutcome(
            stored ?? throw new InvalidOperationException("The completed turn was not durable."),
            wasReplay: false);
    }

    private async Task<TurnResult> ExecuteReservedAsync(
        ConversationTurnRequest request,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var window = await this.windowBuilder
                .BuildAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
            return await this.tracedTurnRunner.RunTracedAsync(
                traceId, request.Message, window, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.turnStore.InterruptTurnAsync(
                request.SessionId, request.RequestId, this.clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await this.turnStore.FailTurnAsync(
                request.SessionId, request.RequestId, this.clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }
}
