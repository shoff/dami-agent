using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Turns;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>
/// One Discord answer: local context, the frontier with its tool bundle, the streamed
/// reply, the pictures it made, the journal entry. Used by the gateway for a message and
/// by scheduled delivery for a job (ADR-0030).
/// </summary>
/// <remarks>
/// There is deliberately no local-model fallback here (ADR-0028). A failure is reported
/// as a failure; the answer is either the frontier's or absent.
/// </remarks>
public sealed class DiscordAnswerer
{
    private readonly IEgressChannel channel;
    private readonly IAugmentedTurn augmented;
    private readonly DiscordReplyStreamer replyStreamer;
    private readonly FrontierToolBundle bundle;
    private readonly DiscordVision vision;
    private readonly IConversationSessionStore sessions;
    private readonly IConversationTurnStore turnStore;
    private readonly IObservationCorpus corpus;
    private readonly ISurfacingQueue surfacings;
    private readonly IStandingLessons lessons;
    private readonly TimeProvider clock;
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordAnswerer> logger;

    /// <summary>Creates the answerer.</summary>
    public DiscordAnswerer(
        IEgressChannel channel,
        IAugmentedTurn augmented,
        DiscordReplyStreamer replyStreamer,
        FrontierToolBundle bundle,
        DiscordVision vision,
        IConversationSessionStore sessions,
        IConversationTurnStore turnStore,
        IObservationCorpus corpus,
        ISurfacingQueue surfacings,
        IStandingLessons lessons,
        TimeProvider clock,
        DiscordOptions options,
        ILogger<DiscordAnswerer> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(augmented);
        ArgumentNullException.ThrowIfNull(replyStreamer);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(lessons);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this.channel = channel;
        this.augmented = augmented;
        this.replyStreamer = replyStreamer;
        this.bundle = bundle;
        this.vision = vision;
        this.sessions = sessions;
        this.turnStore = turnStore;
        this.corpus = corpus;
        this.surfacings = surfacings;
        this.lessons = lessons;
        this.clock = clock;
        this.options = options;
        this.logger = logger;
    }

    private const int NOTICED_LIMIT = 5;
    private const int NOTICED_BODY_CHARS = 400;

    /// <summary>The channel key a scheduled job carries to come back here.</summary>
    public static string DeliveryFor(string conversationId) => "discord:" + conversationId;

    /// <summary>
    /// Answers a message from Steve, captions and all. Only this path records the exchange
    /// into the corpus: a scheduled job's request is Dami's own instruction, not something
    /// Steve said.
    /// </summary>
    public async Task<DiscordAnswerOutcome> AnswerAsync(
        InboundMessage message, string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var captions = await this.vision.DescribeAsync(message, cancellationToken).ConfigureAwait(false);
        return await this.AnswerTurnAsync(message.ConversationId, question, captions, steveMessage, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Answers a question in a conversation, with any locally derived lines as context.</summary>
    /// <returns>
    /// The answer that reached Discord, or the reason none did. A failure here has already
    /// been explained in the conversation; the caller decides whether it is also a failure
    /// of its own — a scheduled job is, a live message is not.
    /// </returns>
    public Task<DiscordAnswerOutcome> AnswerAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> captions,
        CancellationToken cancellationToken) =>
        this.AnswerTurnAsync(conversationId, question, captions, scheduledJob, cancellationToken);

    /// <summary>
    /// Dami's once-a-day check-in: a turn nobody asked for, carrying exactly one surfacing,
    /// which is marked pushed through <paramref name="via"/> once the answer reached Discord.
    /// </summary>
    /// <remarks>
    /// The surfacing is local context like any other, so the disclosure gate judges it before
    /// the frontier sees it; if the gate withholds it, the frontier is told to say only that
    /// something is waiting (ADR-0014 as amended 2026-09-29).
    /// </remarks>
    public Task<DiscordAnswerOutcome> CheckInAsync(
        string conversationId, Surfacing noticed, string via, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(noticed);
        ArgumentException.ThrowIfNullOrWhiteSpace(via);
        return this.AnswerTurnAsync(
            conversationId, CHECK_IN, [], new TurnShape(false, [noticed], via), cancellationToken);
    }

    private const string CHECK_IN =
        "This is your once-a-day check-in, sent to Steve's Discord DM without him asking. Tell him the "
        + "one thing under \"Dami noticed\" in two or three sentences, in your own voice, and why it might "
        + "matter to him. If nothing is listed there, say only that something is waiting in his inbox. "
        + "No greeting filler, no questions about his day.";

    /// <summary>Who started a turn, what it carries from the queue, and how that counts as delivered.</summary>
    /// <param name="FromSteve">Steve wrote the question: it may teach lessons and is recorded into the corpus.</param>
    /// <param name="Noticed">The surfacings to carry; null reads what is pending.</param>
    /// <param name="Via">Null when Steve came to them; the push channel when Dami sent them.</param>
    private sealed record TurnShape(bool FromSteve, IReadOnlyList<Surfacing>? Noticed, string? Via);

    private static readonly TurnShape steveMessage = new(true, null, null);
    private static readonly TurnShape scheduledJob = new(false, null, null);

    private async Task<DiscordAnswerOutcome> AnswerTurnAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> captions,
        TurnShape shape,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(captions);

        var sessionId = DiscordConversations.SessionFor(conversationId);
        await DiscordConversations
            .EnsureAsync(this.sessions, sessionId, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        var prior = await this.PriorExchangesAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var pending = shape.Noticed?.ToList() ?? await this.PendingAsync(cancellationToken).ConfigureAwait(false);
        var lessons = await this.lessons.LinesAsync(cancellationToken).ConfigureAwait(false);
        var localContext = DiscordPrompt.LocalContext(prior, captions, pending.Select(Line).ToList(), lessons);

        // One trace for the tools, the turn's events, the observation, and the line Steve
        // is shown: on 2026-09-16 the answerer minted one id and the turn another, and the
        // id in the error message replayed to nothing.
        var traceId = Guid.NewGuid();
        var outcome = await this.FrontierAsync(
                conversationId, question, localContext, traceId, shape.FromSteve ? question : null, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Answer is not null)
        {
            await this.KeepAsync(sessionId, question, outcome.Answer, traceId, shape.FromSteve, cancellationToken)
                .ConfigureAwait(false);
            await this.MarkDeliveredAsync(pending, shape.Via, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>What an answered turn leaves behind: the journal entry, and Steve's side in the corpus.</summary>
    private async Task KeepAsync(
        Guid sessionId, string question, string answer, Guid traceId, bool fromSteve,
        CancellationToken cancellationToken)
    {
        await this.JournalAsync(sessionId, question, answer, cancellationToken).ConfigureAwait(false);
        if (fromSteve)
        {
            await this.ObserveAsync(question, answer, traceId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What the proactive tier has queued for Steve. It rides his next message rather than
    /// being pushed (ADR-0014 is unsigned): the frontier is told, and if it answered, the
    /// surfacings count as delivered.
    /// </summary>
    private async Task<List<Surfacing>> PendingAsync(CancellationToken cancellationToken)
    {
        var pending = new List<Surfacing>();
        try
        {
            await foreach (var surfacing in this.surfacings.PendingAsync(NOTICED_LIMIT, cancellationToken).ConfigureAwait(false))
            {
                pending.Add(surfacing);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Could not read pending surfacings; answering without them");
        }

        return pending;
    }

    private static string Line(Surfacing surfacing)
    {
        var body = surfacing.Body.Trim().ReplaceLineEndings(" ");
        return $"{surfacing.Title}: {(body.Length > NOTICED_BODY_CHARS ? body[..NOTICED_BODY_CHARS] + "…" : body)}";
    }

    private async Task MarkDeliveredAsync(List<Surfacing> pending, string? via, CancellationToken cancellationToken)
    {
        foreach (var surfacing in pending)
        {
            try
            {
                var now = this.clock.GetUtcNow();
                await (via is null
                    ? this.surfacings.DeliverAsync(surfacing.SurfacingId, now, cancellationToken)
                    : this.surfacings.PushedAsync(surfacing.SurfacingId, via, now, cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.logger.LogWarning(exception, "Could not mark surfacing {Id} delivered", surfacing.SurfacingId);
            }
        }
    }

    /// <returns>The frontier's answer, or the failure once it has been explained.</returns>
    private async Task<DiscordAnswerOutcome> FrontierAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> localContext,
        Guid traceId,
        string? stevesWords,
        CancellationToken cancellationToken)
    {
        var tools = await this.bundle.ForTurnAsync(traceId, DeliveryFor(conversationId), stevesWords, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var answer = await this
                .StreamReplyAsync(conversationId, question, localContext, tools, traceId, cancellationToken)
                .ConfigureAwait(false);
            return DiscordAnswerOutcome.Answered(answer);
        }
        catch (EgressRefusedException refused)
        {
            this.logger.LogWarning("Discord refused a reply: {Reason}", refused.Message);
            await this.channel.SendAsync(DiscordAnswer.Refusal(conversationId, traceId), cancellationToken)
                .ConfigureAwait(false);
            return DiscordAnswerOutcome.Failed(refused.Message);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await this.ReportAsync(
                conversationId, traceId, "it did not answer within its deadline", tools, exception, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await this.ReportAsync(
                conversationId, traceId, "did not answer: " + exception.Message, tools, exception, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<DiscordAnswerOutcome> ReportAsync(
        string conversationId,
        Guid traceId,
        string reason,
        FrontierToolBundle.FrontierTurnTools tools,
        Exception exception,
        CancellationToken cancellationToken)
    {
        this.logger.LogWarning(
            exception, "Frontier turn failed ({Reason}); {Pictures} finished picture(s) delivered with the explanation",
            reason, tools.Pictures.Count);
        var explained = reason.StartsWith("did not answer: ", StringComparison.Ordinal) ? reason["did not answer: ".Length..] : reason;
        await this.ExplainAsync(conversationId, traceId, explained, tools.Pictures, cancellationToken).ConfigureAwait(false);
        return DiscordAnswerOutcome.Failed($"the frontier {reason} (trace {traceId})");
    }

    /// <summary>The frontier answers with the turn's bundle in hand; pictures follow the text.</summary>
    private async Task<string> StreamReplyAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> localContext,
        FrontierToolBundle.FrontierTurnTools tools,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        var stream = await this.augmented
            .StreamAsync(question, localContext, tools.Toolbox, traceId, cancellationToken).ConfigureAwait(false);
        var answer = await this.replyStreamer
            .StreamAsync(conversationId, stream, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Discord turn {Trace} answered by the frontier on {Items} local item(s), {Pictures} picture(s)",
            stream.TraceId, stream.ContextItems, tools.Pictures.Count);

        if (tools.Pictures.Count > 0)
        {
            await this.channel.SendAsync(
                new OutboundContent(conversationId, string.Empty, ContentProvenance.Operational, stream.TraceId)
                {
                    Attachments = tools.Pictures
                        .Select(picture => new OutboundAttachment(picture.FileName, picture.Bytes, picture.ContentType))
                        .ToList(),
                },
                cancellationToken).ConfigureAwait(false);
        }

        return answer;
    }

    /// <summary>
    /// Says why there is no answer — and hands over whatever pictures the turn had
    /// already finished. Eight minutes of portraits were lost to a deadline on 2026-09-16
    /// because they were attached only after a successful stream.
    /// </summary>
    private Task ExplainAsync(
        string conversationId, Guid traceId, string reason, IReadOnlyList<GeneratedImage> pictures,
        CancellationToken cancellationToken)
    {
        var explanation = DiscordAnswer.FrontierUnavailable(conversationId, traceId, reason);
        if (pictures.Count == 0)
        {
            return this.channel.SendAsync(explanation, cancellationToken);
        }

        return this.channel.SendAsync(
            explanation with
            {
                Text = explanation.Text + $" The {pictures.Count} picture(s) it had finished are attached.",
                Attachments = pictures
                    .Select(picture => new OutboundAttachment(picture.FileName, picture.Bytes, picture.ContentType))
                    .ToList(),
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<(string Message, string Response)>> PriorExchangesAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var turns = new List<(string, string)>();
        await foreach (var turn in this.turnStore
            .RecentCompletedTurnsAsync(sessionId, this.options.HistoryTurns, cancellationToken)
            .ConfigureAwait(false))
        {
            turns.Add((turn.Request.Message, turn.Response ?? string.Empty));
        }

        return turns;
    }

    /// <summary>
    /// Records the exchange into the corpus, bounded, so reflection and recall see what
    /// Steve talks about. On 2026-09-16 only the remember tool wrote from Discord: 2
    /// observations in three days of use, and reflection had nothing to reflect on.
    /// </summary>
    private async Task ObserveAsync(
        string question, string answer, Guid traceId, CancellationToken cancellationToken)
    {
        try
        {
            await this.corpus.RecordAsync(
                ChatObservation.Of(question, answer, traceId, this.clock.GetUtcNow(), "discord"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Could not record a Discord exchange into the corpus");
        }
    }

    /// <summary>Records the exchange, so it survives a restart and builds the next window.</summary>
    private async Task JournalAsync(
        Guid sessionId, string question, string answer, CancellationToken cancellationToken)
    {
        try
        {
            var requestId = Guid.NewGuid();
            var now = this.clock.GetUtcNow();
            await this.turnStore.ReserveTurnAsync(
                new ConversationTurnRequest(sessionId, requestId, question, now),
                cancellationToken).ConfigureAwait(false);
            await this.turnStore.CompleteTurnAsync(
                sessionId, requestId, answer, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Steve already has his answer; losing the journal entry costs the next
            // message its memory, which is worth a warning and not a failed turn.
            this.logger.LogWarning(exception, "Could not journal a Discord turn");
        }
    }
}
