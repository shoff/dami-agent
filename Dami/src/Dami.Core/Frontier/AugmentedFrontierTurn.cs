using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>
/// The frontier answers; the local sidecar does the mundane work that feeds it.
/// </summary>
/// <remarks>
/// Retrieval — embedding, ANN, rerank, the recency and grounding gates — all happens
/// on this host, and its output is what the frontier is given to think about. The local
/// model is infrastructure here, not the brain: it never writes the answer.
///
/// The D-012 bridge is the redaction step. Retrieved memory is exactly what must not
/// leave unattended, so before anything egresses the local model rewrites the context
/// with names and identifiers removed, and the exact bytes are stored hash-pinned
/// (ADR-0013's artefact) so what left is auditable after the fact rather than merely
/// promised. Redaction is a default, not a guarantee — <see cref="AugmentedTurnOptions.Gate"/>
/// can disable it, and that is deliberately Steve's decision to make and no one else's.
/// </remarks>
public sealed class AugmentedFrontierTurn
{
    private readonly IContextBuilder contextBuilder;
    private readonly IContextDisclosureGate gate;
    private readonly IFrontierChat frontierChat;
    private readonly IIdentityProvider identityProvider;
    private readonly IEgressBriefStore briefStore;
    private readonly IExecutionEventStore eventStore;
    private readonly AugmentedTurnOptions turnOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<AugmentedFrontierTurn> logger;

    /// <summary>Creates the turn.</summary>
    public AugmentedFrontierTurn(
        IContextBuilder contextBuilder,
        IContextDisclosureGate gate,
        IFrontierChat frontierChat,
        IIdentityProvider identityProvider,
        IEgressBriefStore briefStore,
        IExecutionEventStore eventStore,
        IOptions<AugmentedTurnOptions> turnOptions,
        TimeProvider clock,
        ILogger<AugmentedFrontierTurn> logger)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(frontierChat);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(briefStore);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(turnOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.contextBuilder = contextBuilder;
        this.gate = gate;
        this.frontierChat = frontierChat;
        this.identityProvider = identityProvider;
        this.briefStore = briefStore;
        this.eventStore = eventStore;
        this.turnOptions = turnOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Retrieves locally, then asks the frontier on that context.</summary>
    public async Task<AugmentedTurnResult> RunAsync(
        string question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var traceId = Guid.NewGuid();
        var context = await this.RetrieveAsync(traceId, question, cancellationToken)
            .ConfigureAwait(false);
        var lines = context.Beliefs.Concat(context.Memories).Select(item => item.Content).ToList();
        var prepared = await this.PrepareAsync(question, lines, cancellationToken)
            .ConfigureAwait(false);

        var answer = await this.frontierChat.CompleteAsync(
            new FrontierPrompt(
                prepared, "augmented frontier turn", PrivacyClass.Egressable,
                traceId, ExecutionOrigin.UserTurn),
            cancellationToken).ConfigureAwait(false);

        await this.RecordAsync(traceId, question, prepared, answer, cancellationToken)
            .ConfigureAwait(false);
        return new AugmentedTurnResult(traceId, answer, lines.Count, context.EstimatedTokens);
    }

    private async Task<AssembledContext> RetrieveAsync(
        Guid traceId,
        string question,
        CancellationToken cancellationToken)
    {
        await this.MarkAsync(
            traceId, ExecutionEventType.ContextRetrievalStarted, ExecutionStatus.Running,
            "retrieving locally for a frontier turn", cancellationToken).ConfigureAwait(false);
        var context = await this.contextBuilder.BuildAsync(question, cancellationToken)
            .ConfigureAwait(false);
        await this.MarkAsync(
            traceId, ExecutionEventType.ContextRetrieved, ExecutionStatus.Succeeded,
            $"{context.Memories.Count} memories, {context.Beliefs.Count} beliefs, "
            + $"~{context.EstimatedTokens} tokens", cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <summary>
    /// Runs each retrieved item past the local gate, then composes what survives. Items
    /// the gate disguised go as third-party statements; items it withheld do not go at
    /// all, and the count of each is recorded so the decision is reviewable.
    /// </summary>
    private async Task<string> PrepareAsync(
        string question,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        var decided = this.turnOptions.Gate
            ? await this.gate.ClassifyAsync(question, lines, cancellationToken).ConfigureAwait(false)
            : [.. lines.Select(line => new DisclosedItem(line, Disclosure.Pass, line, "gate disabled"))];

        var sendable = decided.Where(item => item.Disclosure != Disclosure.Withhold).ToList();
        this.logger.LogInformation(
            "Disclosure: {Pass} sent, {Disguise} disguised, {Withheld} withheld",
            sendable.Count(item => item.Disclosure == Disclosure.Pass),
            sendable.Count(item => item.Disclosure == Disclosure.Disguise),
            decided.Count - sendable.Count);

        var prompt = new System.Text.StringBuilder(this.identityProvider.FrontierVoice);
        prompt.AppendLine().AppendLine();
        if (sendable.Count > 0)
        {
            prompt.AppendLine("Context:");
            foreach (var item in sendable)
            {
                prompt.Append("- ").AppendLine(item.Sendable);
            }

            prompt.AppendLine();
        }

        prompt.Append("Question: ").AppendLine(question);
        return prompt.ToString();
    }

    /// <summary>Stores exactly what left, hash-pinned, so it is auditable afterwards.</summary>
    private async Task RecordAsync(
        Guid traceId,
        string question,
        string sent,
        string answer,
        CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        await this.briefStore.CreateAsync(
            new EgressBrief(
                Guid.NewGuid(), null, traceId, question, sent,
                BriefExecutor.HashOf(sent), now, now, answer),
            cancellationToken).ConfigureAwait(false);
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
                "augmented-frontier", type, status, this.clock.GetUtcNow(), label),
            cancellationToken);
    }
}

/// <summary>How an augmented frontier turn treats retrieved memory.</summary>
public sealed class AugmentedTurnOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "AugmentedTurn";

    /// <summary>
    /// Run retrieved context past the local disclosure gate before it egresses. On by
    /// default. Turning it off sends everything retrieved, which is Steve's decision to
    /// take deliberately rather than a default anyone inherits.
    /// </summary>
    public bool Gate { get; set; } = true;
}

/// <summary>What an augmented frontier turn produced.</summary>
public sealed record AugmentedTurnResult(
    Guid TraceId,
    string Answer,
    int ContextItems,
    int EstimatedTokens);
