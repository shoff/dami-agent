using System.Text;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Core.Sessions;
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
public sealed class TurnRunner : ITurnRunner, ITracedTurnRunner
{
    private const string ACTOR = "runtime";

    private readonly IContextBuilder contextBuilder;
    private readonly IModelRouter modelRouter;
    private readonly IChatClient chatClient;
    private readonly IExecutionEventStore eventStore;
    private readonly IObservationCorpus observationCorpus;
    private readonly IIdentityProvider identityProvider;
    private readonly ICapabilitySelectionResolver capabilityResolver;
    private readonly ISkillPromptBuilder skillPromptBuilder;
    private readonly IToolLoopRunner toolLoop;
    private readonly TimeProvider clock;
    private readonly ILogger<TurnRunner> logger;

    /// <summary>Creates the runner.</summary>
    public TurnRunner(
        IContextBuilder contextBuilder,
        IModelRouter modelRouter,
        IChatClient chatClient,
        IExecutionEventStore eventStore,
        IObservationCorpus observationCorpus,
        IIdentityProvider identityProvider,
        ICapabilitySelectionResolver capabilityResolver,
        ISkillPromptBuilder skillPromptBuilder,
        IToolLoopRunner toolLoop,
        TimeProvider clock,
        ILogger<TurnRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(modelRouter);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(observationCorpus);
        ArgumentNullException.ThrowIfNull(capabilityResolver);
        ArgumentNullException.ThrowIfNull(skillPromptBuilder);
        ArgumentNullException.ThrowIfNull(toolLoop);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.contextBuilder = contextBuilder;
        this.modelRouter = modelRouter;
        this.chatClient = chatClient;
        this.eventStore = eventStore;
        this.observationCorpus = observationCorpus;
        this.identityProvider = identityProvider;
        this.capabilityResolver = capabilityResolver;
        this.skillPromptBuilder = skillPromptBuilder;
        this.toolLoop = toolLoop;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<TurnResult> RunAsync(string request, CancellationToken cancellationToken)
    {
        return await this.RunTracedAsync(
            Guid.NewGuid(), request, ConversationWindow.Empty, cancellationToken).ConfigureAwait(false);
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
        if (traceId == Guid.Empty)
        {
            throw new ArgumentException("A traced turn requires a non-empty trace id.", nameof(traceId));
        }

        await this.EmitAsync(
            traceId, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            "turn started", cancellationToken).ConfigureAwait(false);

        try
        {
            return await this.CompleteTurnAsync(
                traceId, request, conversation, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<TurnStream> BeginStreamingAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            "turn started (streaming)", cancellationToken).ConfigureAwait(false);

        try
        {
            return await this.PrepareStreamingAsync(traceId, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.RecordEndAsync(traceId, ExecutionEventType.TraceCancelled,
                ExecutionStatus.Cancelled, "turn cancelled").ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Streaming turn {TraceId} failed", traceId);
            await this.RecordEndAsync(traceId, ExecutionEventType.TraceFailed,
                ExecutionStatus.Failed, $"turn failed: {exception.Message}").ConfigureAwait(false);
            throw;
        }
    }

    private async Task<TurnStream> PrepareStreamingAsync(
        Guid traceId,
        string request,
        CancellationToken cancellationToken)
    {
        var (context, route, prompt, selection) = await this.PrepareAsync(
            traceId, request, ConversationWindow.Empty, cancellationToken)
            .ConfigureAwait(false);
        await this.EmitCapabilitySelectedAsync(
            traceId, Guid.NewGuid(), route, toolCount: 0, selection.Skills.Count, cancellationToken)
            .ConfigureAwait(false);

        // One coalesced streaming event, per the architecture: never one event per token.
        await this.EmitAsync(
            traceId, ExecutionEventType.ResponseStreaming, ExecutionStatus.Running,
            "response streaming", cancellationToken).ConfigureAwait(false);

        return new TurnStream(
            traceId, context, route,
            this.StreamAndFinishAsync(traceId, request, prompt, cancellationToken));
    }

    private async IAsyncEnumerable<string> StreamAndFinishAsync(
        Guid traceId,
        string request,
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var answer = new StringBuilder();

        await foreach (var fragment in this.chatClient.StreamAsync(prompt, cancellationToken)
            .ConfigureAwait(false))
        {
            answer.Append(fragment);
            yield return fragment;
        }

        await this.RecordInteractionAsync(traceId, request, answer.ToString(), cancellationToken)
            .ConfigureAwait(false);
        await this.EmitAsync(
            traceId, ExecutionEventType.TraceCompleted, ExecutionStatus.Succeeded,
            $"answered in {answer.Length} chars (streamed)", cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedTurn> PrepareAsync(
        Guid traceId,
        string request,
        ConversationWindow conversation,
        CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            traceId, ExecutionEventType.ContextRetrievalStarted, ExecutionStatus.Running,
            "assembling context", cancellationToken).ConfigureAwait(false);

        var context = await this.contextBuilder.BuildAsync(request, cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            traceId, ExecutionEventType.ContextRetrieved, ExecutionStatus.Succeeded,
            $"{context.Memories.Count} memories, {context.Beliefs.Count} beliefs, ~{context.EstimatedTokens + conversation.EstimatedTokens} tokens",
            cancellationToken).ConfigureAwait(false);

        // Retrieved context is profile-derived, so the turn is LocalOnly by construction.
        var route = this.modelRouter.Route("synthesis", PrivacyClass.LocalOnly);
        CapabilitySelection selection = await this.capabilityResolver
            .ResolveAsync(request, route.Privacy, cancellationToken).ConfigureAwait(false);
        string skillPrompt = await this.skillPromptBuilder
            .BuildAsync(selection.Skills, cancellationToken).ConfigureAwait(false);
        return new PreparedTurn(
            context,
            route,
            this.BuildPrompt(request, context, conversation, skillPrompt, this.clock.GetUtcNow()),
            selection);
    }

    private async Task<TurnResult> CompleteTurnAsync(
        Guid traceId,
        string request,
        ConversationWindow conversation,
        CancellationToken cancellationToken)
    {
        var result = await this.ExecuteAsync(
            traceId, request, conversation, cancellationToken).ConfigureAwait(false);

        // F-05: interactions are continuously recorded. The turn joins the corpus so
        // the next turn - and the weekly reflection - can see this one happened.
        await this.RecordInteractionAsync(traceId, request, result.Answer, cancellationToken)
            .ConfigureAwait(false);

        await this.EmitAsync(
            traceId, ExecutionEventType.TraceCompleted, ExecutionStatus.Succeeded,
            $"answered in {result.Answer.Length} chars", cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<TurnResult> ExecuteAsync(
        Guid traceId,
        string request,
        ConversationWindow conversation,
        CancellationToken cancellationToken)
    {
        var (context, route, prompt, selection) = await this.PrepareAsync(
            traceId, request, conversation, cancellationToken)
            .ConfigureAwait(false);

        var capabilitySpanId = Guid.NewGuid();
        await this.EmitCapabilitySelectedAsync(
            traceId, capabilitySpanId, route, selection.Tools.Count, selection.Skills.Count,
            cancellationToken).ConfigureAwait(false);
        var answer = await this.toolLoop.RunAsync(
            traceId, capabilitySpanId, prompt, selection.Tools,
            route.Privacy, ExecutionOrigin.UserTurn, cancellationToken).ConfigureAwait(false);

        return new TurnResult(traceId, answer.Trim(), context, route);
    }

    private string BuildPrompt(
        string request,
        AssembledContext context,
        ConversationWindow conversation,
        string skillPrompt,
        DateTimeOffset today)
    {
        var prompt = new StringBuilder();
        // §9.1: the stable identity block leads the prompt — one source, every provider.
        prompt.AppendLine(this.identityProvider.Preamble);
        prompt.AppendLine(
            "Use the context below when it is relevant; say plainly when it is not");
        prompt.AppendLine("sufficient. Be concise and concrete.");
        // The temporal anchor: without it the model treated a March memory as the
        // current crisis. Dated memories are history unless they say otherwise.
        prompt.Append("Today is ").Append(today.ToString("yyyy-MM-dd"))
            .AppendLine(". Context items carry their own dates; anything older than a")
            .AppendLine("few weeks is history and context, not the current situation.");
        prompt.AppendLine();

        prompt.Append(skillPrompt);
        AppendContext(prompt, context);
        AppendConversation(prompt, conversation);

        prompt.AppendLine();
        prompt.Append("Steve: ").AppendLine(request);
        return prompt.ToString();
    }

    private static void AppendConversation(StringBuilder prompt, ConversationWindow conversation)
    {
        if (conversation.Turns.Count == 0)
        {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine("Recent conversation (oldest to newest):");
        foreach (var turn in conversation.Turns)
        {
            prompt.Append("Steve: ").AppendLine(turn.Request.Message);
            prompt.Append("Dami: ").AppendLine(turn.Response);
        }
    }

    private static void AppendContext(StringBuilder prompt, AssembledContext context)
    {
        if (context.Memories.Count == 0)
        {
            prompt.AppendLine(
                "No relevant memories were found for this request. Say so plainly if the");
            prompt.AppendLine(
                "question depends on them - do not guess or invent history.");
        }

        foreach (var belief in context.Beliefs)
        {
            prompt.Append("[belief] ").AppendLine(belief.Content);
        }

        foreach (var memory in context.Memories)
        {
            prompt.Append("[memory ").Append(FormatAsOf(memory.AsOf)).Append("] ")
                .AppendLine(memory.Content);
        }
    }
    /// <summary>Epoch-zero survivors (B10) say so instead of asserting 1970.</summary>
    private static string FormatAsOf(DateTimeOffset asOf)
    {
        return asOf.Year < 1971 ? "undated" : asOf.ToString("yyyy-MM-dd");
    }


    private Task RecordInteractionAsync(
        Guid traceId,
        string request,
        string answer,
        CancellationToken cancellationToken)
    {
        var summary = answer.Length <= 240 ? answer : answer[..240] + "…";
        var observation = new Observation(
            Guid.NewGuid(),
            this.clock.GetUtcNow(),
            "chat",
            $"Steve asked: {request} — Dami answered: {summary}",
            new Dictionary<string, string> { ["trace_id"] = traceId.ToString() });

        return this.observationCorpus.RecordAsync(observation, cancellationToken);
    }

    private Task<long> EmitAsync(
        Guid traceId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        return this.EmitAsync(
            traceId, Guid.NewGuid(), type, status, label, cancellationToken);
    }

    private Task EmitCapabilitySelectedAsync(
        Guid traceId,
        Guid spanId,
        ModelRoute route,
        int toolCount,
        int skillCount,
        CancellationToken cancellationToken)
    {
        return this.EmitAsync(
            traceId, spanId, ExecutionEventType.CapabilitySelected, ExecutionStatus.Succeeded,
            $"{toolCount} tools and {skillCount} skills selected; routed {route.Tier}: {route.Reason}",
            cancellationToken);
    }

    private readonly record struct PreparedTurn(
        AssembledContext Context,
        ModelRoute Route,
        string Prompt,
        CapabilitySelection Selection);

    private Task<long> EmitAsync(
        Guid traceId,
        Guid spanId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            eventId: Guid.NewGuid(),
            traceId: traceId,
            spanId: spanId,
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
