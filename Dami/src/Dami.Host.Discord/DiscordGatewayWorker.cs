using Dami.Contracts.Gateways;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Sessions;
using Dami.Core.Turns;
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
    private readonly ITracedTurnRunner turns;
    private readonly IAugmentedTurn augmented;
    private readonly DiscordVision vision;
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
        ITracedTurnRunner turns,
        IAugmentedTurn augmented,
        DiscordVision vision,
        IConversationSessionStore sessions,
        IConversationTurnStore turnStore,
        IProactiveRunHistory history,
        TimeProvider clock,
        DiscordOptions options,
        ILogger<DiscordGatewayWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(augmented);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.authority = authority;
        this.channel = channel;
        this.turns = turns;
        this.augmented = augmented;
        this.vision = vision;
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

    /// <summary>Sends, or says why it could not — silence would be the wrong failure.</summary>
    private async Task SendOrExplainAsync(
        OutboundContent reply, Guid traceId, CancellationToken cancellationToken)
    {
        try
        {
            await this.channel.SendAsync(reply, cancellationToken).ConfigureAwait(false);
        }
        catch (EgressRefusedException refused)
        {
            this.logger.LogWarning("Discord refused a reply: {Reason}", refused.Message);
            await this.channel.SendAsync(
                DiscordAnswer.Refusal(reply.ConversationId, traceId),
                cancellationToken).ConfigureAwait(false);
        }
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

        var sessionId = DiscordConversations.SessionFor(message.ConversationId);
        await DiscordConversations
            .EnsureAsync(this.sessions, sessionId, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        var localContext = await this.LocalContextAsync(message, sessionId, cancellationToken)
            .ConfigureAwait(false);
        var (answer, traceId, provenance) = await this
            .ThinkAsync(question, localContext, cancellationToken).ConfigureAwait(false);

        await this.SendOrExplainAsync(
            new OutboundContent(message.ConversationId, answer, provenance, traceId),
            traceId, cancellationToken).ConfigureAwait(false);
        await this.JournalAsync(sessionId, question, answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The frontier answers on locally-assembled context; the local model answers only
    /// when the frontier cannot be reached.
    /// </summary>
    /// <remarks>
    /// Falling back rather than failing, because a subscription hiccup should degrade the
    /// answer and not the gateway — and the reply says which model produced it, so a worse
    /// answer is never quietly passed off as the good one.
    /// </remarks>
    private async Task<(string Answer, Guid TraceId, ContentProvenance Provenance)> ThinkAsync(
        string question, IReadOnlyList<string> localContext, CancellationToken cancellationToken)
    {
        if (this.options.Frontier)
        {
            try
            {
                var frontier = await this.augmented
                    .RunAsync(question, localContext, cancellationToken).ConfigureAwait(false);
                this.logger.LogInformation(
                    "Discord turn {Trace} answered by the frontier on {Items} local item(s)",
                    frontier.TraceId, frontier.ContextItems);

                // The augmented turn already gated and redacted everything that left this
                // host, and what came back is the frontier's own prose.
                return (frontier.Answer, frontier.TraceId, ContentProvenance.ProfileDerived);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.logger.LogWarning(exception, "Frontier turn failed; answering locally");
            }
        }

        var traceId = Guid.NewGuid();
        var local = await this.turns
            .RunTracedAsync(traceId, question, ConversationWindow.Empty, cancellationToken)
            .ConfigureAwait(false);

        // The worker labels; the channel decides (ADR-0025). Keeping the judgement in one
        // place stops the two disagreeing, which is how the gateway ended up refusing a
        // greeting while believing it was enforcing D-012.
        var note = this.options.Frontier ? "\n\n_(answered locally — the frontier was unreachable)_" : string.Empty;
        return (local.Answer + note, local.TraceId, DiscordAnswer.ProvenanceOf(local));
    }

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
