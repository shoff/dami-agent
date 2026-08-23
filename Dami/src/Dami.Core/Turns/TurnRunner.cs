using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Turns;

/// <summary>The interactive turn, assembled from the pieces already proven.</summary>
/// <remarks>
/// The charter's Phase 2 exit shape: a prompt travels through context assembly, a
/// routing decision, and a model, and comes back as a truthful trace plus an answer.
/// Every stage is an event with <see cref="ExecutionOrigin.UserTurn"/>; the context's
/// token cost and the route's reason ride in the labels, and the prompt text never does.
///
/// A prompt containing retrieved memories or beliefs is LocalOnly by construction
/// (ADR-0010 §5), so this runner classifies every context-bearing turn LocalOnly and
/// routes it to the sidecar. Frontier turns become possible when a redaction step
/// exists; that is a future ADR, not a flag.
/// </remarks>
public sealed class TurnRunner : ITurnRunner
{
    private const string ACTOR = "runtime";

    private readonly IContextBuilder contextBuilder;
    private readonly IModelRouter modelRouter;
    private readonly IChatClient chatClient;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<TurnRunner> logger;

    /// <summary>Creates the runner.</summary>
    public TurnRunner(
        IContextBuilder contextBuilder,
        IModelRouter modelRouter,
        IChatClient chatClient,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<TurnRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(modelRouter);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.contextBuilder = contextBuilder;
        this.modelRouter = modelRouter;
        this.chatClient = chatClient;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<TurnResult> RunAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            "turn started", cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await this.ExecuteAsync(traceId, request, cancellationToken).ConfigureAwait(false);

            await this.EmitAsync(
                traceId, ExecutionEventType.TraceCompleted, ExecutionStatus.Succeeded,
                $"answered in {result.Answer.Length} chars", cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.RecordEndAsync(traceId, ExecutionEventType.TraceCancelled,
                ExecutionStatus.Cancelled, "turn cancelled").ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Turn {TraceId} failed", traceId);
            await this.RecordEndAsync(traceId, ExecutionEventType.TraceFailed,
                ExecutionStatus.Failed, $"turn failed: {exception.Message}").ConfigureAwait(false);
            throw;
        }
    }

    private Task RecordEndAsync(
        Guid traceId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label)
    {
        // CancellationToken.None: the turn's token may already be cancelled, and the
        // record of how it ended must still be written.
        return this.EmitAsync(traceId, type, status, label, CancellationToken.None);
    }

    private async Task<TurnResult> ExecuteAsync(
        Guid traceId,
        string request,
        CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            traceId, ExecutionEventType.ContextRetrievalStarted, ExecutionStatus.Running,
            "assembling context", cancellationToken).ConfigureAwait(false);

        var context = await this.contextBuilder.BuildAsync(request, cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            traceId, ExecutionEventType.ContextRetrieved, ExecutionStatus.Succeeded,
            $"{context.Memories.Count} memories, {context.Beliefs.Count} beliefs, ~{context.EstimatedTokens} tokens",
            cancellationToken).ConfigureAwait(false);

        // Retrieved context is profile-derived, so the turn is LocalOnly by construction.
        var route = this.modelRouter.Route("synthesis", PrivacyClass.LocalOnly);
        await this.EmitAsync(
            traceId, ExecutionEventType.CapabilitySelected, ExecutionStatus.Succeeded,
            $"routed {route.Tier}: {route.Reason}", cancellationToken).ConfigureAwait(false);

        var answer = await this.chatClient
            .CompleteAsync(BuildPrompt(request, context, this.clock.GetUtcNow()), cancellationToken)
            .ConfigureAwait(false);

        return new TurnResult(traceId, answer.Trim(), context, route);
    }

    private static string BuildPrompt(string request, AssembledContext context, DateTimeOffset today)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(
            "You are Dami, Steve's assistant. Use the context below when it is relevant;");
        prompt.AppendLine(
            "say plainly when it is not sufficient. Be concise and concrete.");
        // The temporal anchor: without it the model treated a March memory as the
        // current crisis. Dated memories are history unless they say otherwise.
        prompt.Append("Today is ").Append(today.ToString("yyyy-MM-dd"))
            .AppendLine(". Context items carry their own dates; anything older than a")
            .AppendLine("few weeks is history and context, not the current situation.");
        prompt.AppendLine();

        foreach (var belief in context.Beliefs)
        {
            prompt.Append("[belief] ").AppendLine(belief.Content);
        }

        foreach (var memory in context.Memories)
        {
            prompt.Append("[memory ").Append(memory.AsOf.ToString("yyyy-MM-dd")).Append("] ")
                .AppendLine(memory.Content);
        }

        prompt.AppendLine();
        prompt.Append("Steve: ").AppendLine(request);
        return prompt.ToString();
    }

    private Task<long> EmitAsync(
        Guid traceId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            eventId: Guid.NewGuid(),
            traceId: traceId,
            spanId: Guid.NewGuid(),
            parentSpanId: null,
            origin: ExecutionOrigin.UserTurn,
            actorId: ACTOR,
            type: type,
            status: status,
            occurredAt: this.clock.GetUtcNow(),
            label: label);

        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
