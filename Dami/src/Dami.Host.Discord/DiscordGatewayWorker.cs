using Dami.Contracts.Gateways;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
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
    private readonly IProactiveRunHistory history;
    private readonly TimeProvider clock;
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordGatewayWorker> logger;

    /// <summary>Creates the worker.</summary>
    public DiscordGatewayWorker(
        IGatewayAuthority authority,
        IEgressChannel channel,
        ITracedTurnRunner turns,
        IProactiveRunHistory history,
        TimeProvider clock,
        DiscordOptions options,
        ILogger<DiscordGatewayWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.authority = authority;
        this.channel = channel;
        this.turns = turns;
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

    private async Task AnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (await this.TryOperationalAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var traceId = Guid.NewGuid();
        var result = await this.turns
            .RunTracedAsync(traceId, message.Text, ConversationWindow.Empty, cancellationToken)
            .ConfigureAwait(false);

        // The worker labels; the channel decides (ADR-0025). Keeping the judgement in one
        // place stops the two disagreeing, which is how the gateway ended up refusing a
        // greeting while believing it was enforcing D-012.
        var provenance = DiscordAnswer.ProvenanceOf(result);
        var reply = new OutboundContent(
            message.ConversationId, result.Answer, provenance, result.TraceId);

        await this.SendOrExplainAsync(reply, result.TraceId, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Discord turn {Trace} answered as {Provenance}", result.TraceId, provenance);
    }
}
