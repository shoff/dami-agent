using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>Asks the frontier a question the local sidecar has prepared.</summary>
/// <remarks>
/// An interface so callers can be tested without standing up retrieval, the disclosure
/// gate, a brief store, and a frontier process. <see cref="AugmentedFrontierTurn"/> is
/// the only implementation.
/// </remarks>
public interface IAugmentedTurn
{
    /// <summary>Retrieves locally, redacts locally, and answers at the frontier.</summary>
    Task<AugmentedTurnResult> RunAsync(string question, CancellationToken cancellationToken) =>
        this.RunAsync(question, [], cancellationToken);

    /// <summary>The same, carrying context the caller derived on this host.</summary>
    /// <remarks>
    /// Everything in <paramref name="localContext"/> goes through the disclosure gate with
    /// retrieved memory rather than around it — prior exchanges, image captions, anything
    /// this host produced about Steve. The question is his own words and is appended
    /// ungated; anything derived belongs here instead, because a caller that folds derived
    /// text into the question egresses it unjudged.
    /// </remarks>
    Task<AugmentedTurnResult> RunAsync(
        string question,
        IReadOnlyList<string> localContext,
        CancellationToken cancellationToken);

    /// <summary>The same turn, with the answer streamed as the frontier writes it.</summary>
    /// <remarks>
    /// Retrieval and the gate still complete first — nothing may leave until the gate has
    /// judged all of it — so the caller waits for those, then watches the answer arrive.
    /// The brief is written once the stream drains, so what left is still recorded.
    /// </remarks>
    Task<AugmentedTurnStream> StreamAsync(
        string question,
        IReadOnlyList<string> localContext,
        CancellationToken cancellationToken);
}

/// <summary>A streaming augmented turn: what was assembled, and the answer as it arrives.</summary>
public sealed record AugmentedTurnStream(
    Guid TraceId,
    int ContextItems,
    int EstimatedTokens,
    IAsyncEnumerable<string> Tokens);

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
public sealed class AugmentedFrontierTurn : IAugmentedTurn
{
    private readonly IContextBuilder contextBuilder;
    private readonly IContextDisclosureGate gate;
    private readonly IFrontierChat frontierChat;
    private readonly IIdentityProvider identityProvider;
    private readonly IEgressBriefStore briefStore;
    private readonly IDisclosureLedger disclosureLedger;
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
        IDisclosureLedger disclosureLedger,
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
        ArgumentNullException.ThrowIfNull(disclosureLedger);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(turnOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.contextBuilder = contextBuilder;
        this.gate = gate;
        this.frontierChat = frontierChat;
        this.identityProvider = identityProvider;
        this.briefStore = briefStore;
        this.disclosureLedger = disclosureLedger;
        this.eventStore = eventStore;
        this.turnOptions = turnOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Retrieves locally, then asks the frontier on that context.</summary>
    public Task<AugmentedTurnResult> RunAsync(
        string question,
        CancellationToken cancellationToken) =>
        this.RunAsync(question, [], cancellationToken);

    /// <inheritdoc />
    public async Task<AugmentedTurnResult> RunAsync(
        string question,
        IReadOnlyList<string> localContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(localContext);

        var traceId = Guid.NewGuid();
        var context = await this.RetrieveAsync(traceId, question, cancellationToken)
            .ConfigureAwait(false);
        var lines = localContext
            .Concat(context.Beliefs.Concat(context.Memories).Select(item => item.Content))
            .ToList();
        var prepared = await this.PrepareAsync(traceId, question, lines, cancellationToken)
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

    /// <inheritdoc />
    public async Task<AugmentedTurnStream> StreamAsync(
        string question,
        IReadOnlyList<string> localContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(localContext);

        var traceId = Guid.NewGuid();
        var context = await this.RetrieveAsync(traceId, question, cancellationToken)
            .ConfigureAwait(false);
        var lines = localContext
            .Concat(context.Beliefs.Concat(context.Memories).Select(item => item.Content))
            .ToList();
        var prepared = await this.PrepareAsync(traceId, question, lines, cancellationToken)
            .ConfigureAwait(false);

        return new AugmentedTurnStream(
            traceId, lines.Count, context.EstimatedTokens,
            this.StreamAnswerAsync(traceId, question, prepared, cancellationToken));
    }

    /// <summary>Streams the frontier's answer, recording what left once it has all arrived.</summary>
    private async IAsyncEnumerable<string> StreamAnswerAsync(
        Guid traceId,
        string question,
        string prepared,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var answer = new System.Text.StringBuilder();
        await foreach (var fragment in this.frontierChat.StreamAsync(
                new FrontierPrompt(
                    prepared, "augmented frontier turn", PrivacyClass.Egressable,
                    traceId, ExecutionOrigin.UserTurn),
                cancellationToken).ConfigureAwait(false))
        {
            answer.Append(fragment);
            yield return fragment;
        }

        await this.RecordAsync(traceId, question, prepared, answer.ToString(), cancellationToken)
            .ConfigureAwait(false);
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
        Guid traceId,
        string question,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        var decided = await this.DecideAsync(traceId, question, lines, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The gate's verdict per item, recorded so it can be reviewed and corrected — the
    /// corrections are what the gate reads back as examples (G9a). A disabled gate
    /// records nothing: there was no decision to correct.
    /// </summary>
    private async Task<IReadOnlyList<DisclosedItem>> DecideAsync(
        Guid traceId,
        string question,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (!this.turnOptions.Gate)
        {
            return [.. lines.Select(line => new DisclosedItem(line, Disclosure.Pass, line, "gate disabled"))];
        }

        var decided = await this.gate.ClassifyAsync(question, lines, cancellationToken).ConfigureAwait(false);
        await this.disclosureLedger.RecordAsync(
            traceId, question, decided, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return decided;
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
