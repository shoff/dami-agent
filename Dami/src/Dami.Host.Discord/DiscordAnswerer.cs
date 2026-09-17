using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
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

    /// <summary>Answers a message from Steve, captions and all.</summary>
    public async Task<DiscordAnswerOutcome> AnswerAsync(
        InboundMessage message, string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var captions = await this.vision.DescribeAsync(message, cancellationToken).ConfigureAwait(false);
        return await this.AnswerAsync(message.ConversationId, question, captions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Answers a question in a conversation, with any locally derived lines as context.</summary>
    /// <returns>
    /// The answer that reached Discord, or the reason none did. A failure here has already
    /// been explained in the conversation; the caller decides whether it is also a failure
    /// of its own — a scheduled job is, a live message is not.
    /// </returns>
    public async Task<DiscordAnswerOutcome> AnswerAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> captions,
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
        var pending = await this.PendingAsync(cancellationToken).ConfigureAwait(false);
        var lessons = await this.lessons.LinesAsync(cancellationToken).ConfigureAwait(false);
        var localContext = DiscordPrompt.LocalContext(prior, captions, pending.Select(Line).ToList(), lessons);

        var outcome = await this.FrontierAsync(conversationId, question, localContext, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Answer is not null)
        {
            await this.JournalAsync(sessionId, question, outcome.Answer, cancellationToken).ConfigureAwait(false);
            await this.MarkDeliveredAsync(pending, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
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

    private async Task MarkDeliveredAsync(List<Surfacing> pending, CancellationToken cancellationToken)
    {
        foreach (var surfacing in pending)
        {
            try
            {
                await this.surfacings.DeliverAsync(surfacing.SurfacingId, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        try
        {
            var answer = await this
                .StreamReplyAsync(conversationId, question, localContext, traceId, cancellationToken)
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
            const string reason = "it did not answer within its deadline";
            this.logger.LogWarning(exception, "Frontier turn timed out; nothing was answered");
            await this.ExplainAsync(conversationId, traceId, reason, cancellationToken)
                .ConfigureAwait(false);
            return DiscordAnswerOutcome.Failed($"the frontier {reason} (trace {traceId})");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Frontier turn failed; nothing was answered");
            await this.ExplainAsync(conversationId, traceId, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return DiscordAnswerOutcome.Failed($"the frontier did not answer: {exception.Message} (trace {traceId})");
        }
    }

    /// <summary>The frontier answers with the turn's bundle in hand; pictures follow the text.</summary>
    private async Task<string> StreamReplyAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> localContext,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        var tools = await this.bundle.ForTurnAsync(traceId, DeliveryFor(conversationId), cancellationToken)
            .ConfigureAwait(false);
        var stream = await this.augmented
            .StreamAsync(question, localContext, tools.Toolbox, cancellationToken).ConfigureAwait(false);
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

    private Task ExplainAsync(
        string conversationId, Guid traceId, string reason, CancellationToken cancellationToken) =>
        this.channel.SendAsync(
            DiscordAnswer.FrontierUnavailable(conversationId, traceId, reason), cancellationToken);

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
