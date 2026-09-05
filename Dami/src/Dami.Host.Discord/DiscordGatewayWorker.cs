using Dami.Contracts.Gateways;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Runs the Discord gateway, if this process is the one allowed to (M1).</summary>
/// <remarks>
/// Authority is taken, not assumed. Two bots on one token answer every message twice and
/// neither process can see the other doing it, so a worker that cannot acquire the lease
/// refuses to serve rather than running "probably alone".
/// </remarks>
public sealed class DiscordGatewayWorker : BackgroundService
{
    private const string GATEWAY = "discord";

    private readonly IGatewayAuthority authority;
    private readonly IEgressChannel channel;
    private readonly DiscordReplyStreamer replyStreamer;
    private readonly IAugmentedTurn augmented;
    private readonly DiscordVision vision;
    private readonly DiscordImageResponder images;
    private readonly DiscordToolbox toolbox;
    private readonly DiscordTypingIndicator typing;
    private readonly IConversationSessionStore sessions;
    private readonly IConversationTurnStore turnStore;
    private readonly IProactiveRunHistory history;
    private readonly TimeProvider clock;
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordGatewayWorker> logger;

    /// <summary>Creates the worker.</summary>
    public DiscordGatewayWorker(
        IGatewayAuthority authority,
        IEgressChannel channel,
        IAugmentedTurn augmented,
        DiscordVision vision,
        DiscordReplyStreamer replyStreamer,
        DiscordImageResponder images,
        DiscordToolbox toolbox,
        DiscordTypingIndicator typing,
        IConversationSessionStore sessions,
        IConversationTurnStore turnStore,
        IProactiveRunHistory history,
        TimeProvider clock,
        DiscordOptions options,
        ILogger<DiscordGatewayWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(augmented);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(replyStreamer);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(typing);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.authority = authority;
        this.channel = channel;
        this.augmented = augmented;
        this.vision = vision;
        this.replyStreamer = replyStreamer;
        this.images = images;
        this.toolbox = toolbox;
        this.typing = typing;
        this.sessions = sessions;
        this.turnStore = turnStore;
        this.history = history;
        this.clock = clock;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.options.IsConfigured)
        {
            this.logger.LogInformation(
                "Discord gateway is not configured (needs Discord__Token and Discord__OwnerUserId); not starting");
            return;
        }

        await using var lease = await this.authority
            .TryAcquireAsync(GATEWAY, stoppingToken)
            .ConfigureAwait(false);

        if (lease is null)
        {
            this.logger.LogWarning(
                "Another process holds the {Gateway} gateway; this one will not serve", GATEWAY);
            return;
        }

        this.logger.LogInformation("Discord gateway has authority; listening");
        await this.ListenAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in this.channel.ListenAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await this.AnswerAsync(message, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException exception)
            {
                // A component deadline cancels one message, not the long-lived gateway.
                this.logger.LogError(exception, "Discord turn canceled before completion");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One bad turn must not end the gateway. The tool loop learned this the
                // expensive way: a single failure killed every turn after it.
                this.logger.LogError(exception, "Discord turn failed");
            }
        }
    }

    /// <summary>
    /// Answers the questions that never touch the profile, straight from runtime state.
    /// </summary>
    /// <remarks>
    /// Tried before the general path rather than after a refusal, because the general path
    /// assembles context on the way — asking it first would retrieve Steve's memories in
    /// order to answer "status", which is the opposite of the point.
    /// </remarks>
    private async Task<bool> TryOperationalAsync(
        InboundMessage message, CancellationToken cancellationToken)
    {
        var intent = DiscordOperations.Classify(message.Text);
        if (intent == DiscordOperations.Intent.None)
        {
            return false;
        }

        var text = intent == DiscordOperations.Intent.Help
            ? DiscordOperations.Help()
            : DiscordOperations.Status(
                await this.history.ReadAsync(5, cancellationToken).ConfigureAwait(false),
                this.clock.GetUtcNow());

        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, text, ContentProvenance.Operational, Guid.Empty),
            cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation("Discord answered {Intent} from runtime state", intent);
        return true;
    }

    /// <summary>
    /// Answers one message: local models look and remember, the frontier thinks (ADR-0026).
    /// </summary>
    private async Task AnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (await this.TryOperationalAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var question = DiscordPrompt.Question(message);
        if (question.Length == 0)
        {
            return;
        }

        await using var active = await this.typing
            .BeginAsync(message.ConversationId, cancellationToken).ConfigureAwait(false);
        await this.AnswerQuestionAsync(message, question, cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerQuestionAsync(
        InboundMessage message, string question, CancellationToken cancellationToken)
    {
        if (await this.images.TryAnswerAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var sessionId = DiscordConversations.SessionFor(message.ConversationId);
        await DiscordConversations
            .EnsureAsync(this.sessions, sessionId, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        var localContext = await this.LocalContextAsync(message, sessionId, cancellationToken).ConfigureAwait(false);
        var answer = await this
            .FrontierAsync(message.ConversationId, question, localContext, cancellationToken)
            .ConfigureAwait(false);
        if (answer is not null)
        {
            await this.JournalAsync(sessionId, question, answer, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The frontier answers on locally-assembled context, or the gateway says that it
    /// could not. Nothing else writes a reply (ADR-0028).
    /// </summary>
    /// <remarks>
    /// There is deliberately no local-model fallback here. The one ADR-0026 kept "for a
    /// subscription hiccup" fired on a Discord 429 and on a ten-minute Codex hang, and
    /// each time Steve got a qwen3 answer he had said he never wanted. A failure is
    /// reported as a failure; the answer is either the frontier's or absent.
    /// </remarks>
    /// <returns>The frontier's answer, or null when the failure was already explained.</returns>
    private async Task<string?> FrontierAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> localContext,
        CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        try
        {
            return await this.StreamReplyAsync(conversationId, question, localContext, traceId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EgressRefusedException refused)
        {
            this.logger.LogWarning("Discord refused a reply: {Reason}", refused.Message);
            await this.channel.SendAsync(DiscordAnswer.Refusal(conversationId, traceId), cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(exception, "Frontier turn timed out; nothing was answered");
            await this.ExplainAsync(conversationId, traceId, "it did not answer within its deadline", cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Frontier turn failed; nothing was answered");
            await this.ExplainAsync(conversationId, traceId, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// The frontier answers with the turn's tool bundle in hand (ADR-0030); pictures it
    /// made on the way follow the text as attachments.
    /// </summary>
    private async Task<string> StreamReplyAsync(
        string conversationId,
        string question,
        IReadOnlyList<string> localContext,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        var tools = this.toolbox.ForTurn(traceId);
        var stream = await this.augmented
            .StreamAsync(question, localContext, tools.Toolbox, cancellationToken).ConfigureAwait(false);
        var answer = await this.replyStreamer
            .StreamAsync(conversationId, stream, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Discord turn {Trace} answered by the frontier on {Items} local item(s), {Pictures} picture(s)",
            stream.TraceId, stream.ContextItems, tools.Attachments.Count);

        if (tools.Attachments.Count > 0)
        {
            await this.channel.SendAsync(
                new OutboundContent(conversationId, string.Empty, ContentProvenance.Operational, stream.TraceId)
                {
                    Attachments = tools.Attachments,
                },
                cancellationToken).ConfigureAwait(false);
        }

        return answer;
    }

    /// <summary>Says why there is no answer — silence would be the wrong failure.</summary>
    private Task ExplainAsync(
        string conversationId, Guid traceId, string reason, CancellationToken cancellationToken) =>
        this.channel.SendAsync(
            DiscordAnswer.FrontierUnavailable(conversationId, traceId, reason), cancellationToken);

    /// <summary>
    /// Everything this host derived for the turn: the recent conversation, so the next
    /// message is not turn one again, and captions of any images.
    /// </summary>
    /// <remarks>
    /// Captions are derived from LocalOnly images, so they belong in the gated context
    /// rather than in the question — the question is appended to the frontier prompt
    /// ungated, and a caption there would leave the host unjudged.
    /// </remarks>
    private async Task<IReadOnlyList<string>> LocalContextAsync(
        InboundMessage message, Guid sessionId, CancellationToken cancellationToken)
    {
        var captions = await this.vision.DescribeAsync(message, cancellationToken)
            .ConfigureAwait(false);
        var prior = await this.PriorExchangesAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        return DiscordPrompt.LocalContext(prior, captions);
    }

    /// <summary>The recent conversation, so the next message is not turn one again.</summary>
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
