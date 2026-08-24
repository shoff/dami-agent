using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>
/// Runs a durable session turn on the subscription frontier (ADR-0011) instead of the
/// local sidecar.
/// </summary>
/// <remarks>
/// It plugs into the same <see cref="ITracedTurnRunner"/> seam the local runner uses, so
/// every session guarantee — idempotent reservation, interruption, replay, durable
/// completion — applies unchanged; only the model differs.
///
/// The privacy rule this type exists to enforce: a frontier turn carries **only
/// conversation that already went to the frontier**. A session can mix local and
/// frontier turns, and a local answer may quote retrieved memories; replaying that
/// history outward would egress those memories without consent, which is exactly what
/// D-012 forbids. Prior turns are therefore included only when their own trace shows a
/// completed egress. Nothing else about the session changes.
/// </remarks>
public sealed class FrontierTracedTurnRunner : ITracedTurnRunner
{
    private readonly IFrontierChat frontierChat;
    private readonly IIdentityProvider identityProvider;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<FrontierTracedTurnRunner> logger;

    /// <summary>Creates the runner.</summary>
    public FrontierTracedTurnRunner(
        IFrontierChat frontierChat,
        IIdentityProvider identityProvider,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<FrontierTracedTurnRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(frontierChat);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.frontierChat = frontierChat;
        this.identityProvider = identityProvider;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<TurnResult> RunTracedAsync(
        Guid traceId,
        string request,
        ConversationWindow conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(conversation);

        var sendable = await this.EgressedTurnsAsync(conversation, cancellationToken)
            .ConfigureAwait(false);
        await this.MarkAsync(
            traceId, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            $"frontier session turn ({sendable.Count} prior exchange(s) carried)", cancellationToken)
            .ConfigureAwait(false);

        var answer = await this.frontierChat.CompleteAsync(
            new FrontierPrompt(
                this.BuildPrompt(request, sendable), "frontier session turn",
                PrivacyClass.Egressable, traceId, ExecutionOrigin.UserTurn),
            cancellationToken).ConfigureAwait(false);

        await this.MarkAsync(
            traceId, ExecutionEventType.TraceCompleted, ExecutionStatus.Succeeded,
            $"frontier session turn: {answer.Length} chars", cancellationToken).ConfigureAwait(false);

        return new TurnResult(
            traceId,
            answer,
            new AssembledContext([], [], 0),
            new ModelRoute(
                ModelTier.Frontier, PrivacyClass.Egressable,
                "answered on the subscription frontier; no retrieved memory was sent"));
    }

    private string BuildPrompt(string request, IReadOnlyList<Contracts.Sessions.ConversationTurn> prior)
    {
        var prompt = new StringBuilder(this.identityProvider.FrontierVoice);
        prompt.AppendLine().AppendLine();

        if (prior.Count > 0)
        {
            prompt.AppendLine("Conversation so far:");
            foreach (var turn in prior)
            {
                prompt.Append("Steve: ").AppendLine(turn.Request.Message);
                prompt.Append("You: ").AppendLine(turn.Response);
            }

            prompt.AppendLine();
        }

        prompt.Append("Steve: ").AppendLine(request);
        return prompt.ToString();
    }

    /// <summary>Prior turns whose own trace shows a completed egress — and only those.</summary>
    private async Task<IReadOnlyList<Contracts.Sessions.ConversationTurn>> EgressedTurnsAsync(
        ConversationWindow conversation,
        CancellationToken cancellationToken)
    {
        var carried = new List<Contracts.Sessions.ConversationTurn>();
        foreach (var turn in conversation.Turns)
        {
            if (turn.Response is null)
            {
                continue;
            }

            if (await this.WentOutAsync(turn.TraceId, cancellationToken).ConfigureAwait(false))
            {
                carried.Add(turn);
            }
        }

        var withheld = conversation.Turns.Count - carried.Count;
        if (withheld > 0)
        {
            this.logger.LogInformation(
                "Frontier turn withheld {Count} local exchange(s) from the prompt (D-012)", withheld);
        }

        return carried;
    }

    private async Task<bool> WentOutAsync(Guid traceId, CancellationToken cancellationToken)
    {
        await foreach (var item in this.eventStore.ReplayAsync(traceId, cancellationToken)
            .ConfigureAwait(false))
        {
            if (item.Type == ExecutionEventType.EgressCompleted)
            {
                return true;
            }
        }

        return false;
    }

    private Task MarkAsync(
        Guid traceId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        return this.eventStore.AppendAsync(
            new ExecutionEvent(
                Guid.NewGuid(), traceId, Guid.NewGuid(), null, ExecutionOrigin.UserTurn,
                "frontier-session", type, status, this.clock.GetUtcNow(), label),
            cancellationToken);
    }
}
