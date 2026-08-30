using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging;

namespace Dami.Gateway.Discord;

/// <summary>Discord as an egress channel (ADR-0024).</summary>
/// <remarks>
/// Everything that decides what may leave is in <see cref="ChannelDisclosurePolicy"/>,
/// which is pure; everything here is transport. That split is deliberate — transport
/// grows special cases under pressure, and the boundary must not be somewhere that
/// happens.
/// </remarks>
public sealed class DiscordEgressChannel : IEgressChannel
{
    private static readonly Uri gateway = new("wss://gateway.discord.gg/?v=10&encoding=json");

    private readonly Func<IDiscordSocket> connect;
    private readonly IDiscordRest rest;
    private readonly DiscordOptions options;
    private static readonly TimeSpan identifyFloor = TimeSpan.FromSeconds(5);

    private readonly DiscordSession session = new();
    private DateTimeOffset lastIdentify = DateTimeOffset.MinValue;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordEgressChannel> logger;

    /// <summary>Creates the channel.</summary>
    public DiscordEgressChannel(
        Func<IDiscordSocket> connect,
        IDiscordRest rest,
        DiscordOptions options,
        TimeProvider clock,
        ILogger<DiscordEgressChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.connect = connect;
        this.rest = rest;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ChannelName => "discord";

    /// <summary>The bot's own user id, once READY has been seen.</summary>
    public string SelfId => this.session.SelfId;

    /// <inheritdoc />
    public async Task SendAsync(OutboundContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // ADR-0025: inbound is already filtered to OwnerUserId, so the only conversation
        // this channel ever replies into is Steve's own. The recipient is the subject.
        ChannelDisclosurePolicy.EnsureMayLeave(content, this.ChannelName, recipientIsDataSubject: true);
        await this.rest
            .PostMessageAsync(content.ConversationId, content.Text, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<InboundMessage> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pipe = Channel.CreateBounded<InboundMessage>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

        var pump = Task.Run(() => this.PumpAsync(pipe.Writer, cancellationToken), CancellationToken.None);

        await foreach (var message in pipe.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }

        await pump.ConfigureAwait(false);
    }

    /// <summary>Reconnects for as long as the caller is listening.</summary>
    /// <remarks>
    /// Exponential backoff capped at a minute. A gateway that reconnects in a tight loop
    /// against a refusing server is indistinguishable from an attack, and Discord will
    /// treat it as one.
    /// </remarks>
    private async Task PumpAsync(ChannelWriter<InboundMessage> writer, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            var startedAt = this.clock.GetUtcNow();
            if (await this.AttemptAsync(writer, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            // Only a connection that actually lived resets the backoff. A socket that dies
            // immediately is a failure however cleanly it returned, and treating it as a
            // success is what turns exponential backoff into a tight loop against Discord.
            if (this.clock.GetUtcNow() - startedAt > TimeSpan.FromSeconds(60))
            {
                backoff = TimeSpan.FromSeconds(1);
            }

            if (!await DelayAsync(backoff, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            backoff = backoff < TimeSpan.FromMinutes(1) ? backoff * 2 : TimeSpan.FromMinutes(1);
        }

        writer.TryComplete();
    }

    /// <summary>One connection attempt.</summary>
    /// <returns>True when the gateway must stop for good rather than retry.</returns>
    private async Task<bool> AttemptAsync(
        ChannelWriter<InboundMessage> writer, CancellationToken cancellationToken)
    {
        try
        {
            var fatal = await this.RunConnectionAsync(writer, cancellationToken).ConfigureAwait(false);
            if (fatal is null)
            {
                return false;
            }

            this.logger.LogError(
                "Discord closed the gateway permanently ({Code} {Description}). {Advice}",
                fatal.Code,
                fatal.Description,
                fatal.Advice);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Discord gateway connection failed; retrying");
            return false;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>One connection, from HELLO to the socket closing.</summary>
    /// <returns>The close reason if it was fatal, otherwise null.</returns>
    private async Task<DiscordClose?> RunConnectionAsync(
        ChannelWriter<InboundMessage> writer, CancellationToken cancellationToken)
    {
        await using var socket = this.connect();
        await socket.ConnectAsync(this.Destination(), cancellationToken).ConfigureAwait(false);

        var interval = await this.HandshakeAsync(socket, cancellationToken).ConfigureAwait(false);
        if (interval is null)
        {
            return this.ClosedEarly(socket);
        }

        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var beat = Task.Run(
            () => this.HeartbeatAsync(socket, interval.Value, connection.Token), CancellationToken.None);

        try
        {
            await this.ReceiveLoopAsync(socket, writer, connection, cancellationToken).ConfigureAwait(false);
            return Fatal(socket);
        }
        finally
        {
            await connection.CancelAsync().ConfigureAwait(false);
            try
            {
                await beat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the heartbeat is cancelled when the connection ends.
            }
        }
    }

    /// <summary>
    /// Where to connect. A resume must go to the URL READY named; the generic gateway
    /// answers a resume with INVALID_SESSION, which presents as a loop rather than a
    /// mistake.
    /// </summary>
    private Uri Destination() =>
        this.session.CanResume && this.session.ResumeGateway is { } resume ? resume : gateway;

    /// <summary>
    /// Closed before HELLO. The reason matters more than the fact: a fatal code here must
    /// stop the gateway rather than start the loop again.
    /// </summary>
    private DiscordClose? ClosedEarly(IDiscordSocket socket)
    {
        this.logger.LogWarning(
            "Discord closed before HELLO ({Reason})",
            socket.CloseReason?.Description ?? "no reason given");
        return Fatal(socket);
    }

    /// <summary>A close that can never succeed on retry, or null.</summary>
    private static DiscordClose? Fatal(IDiscordSocket socket) =>
        socket.CloseReason is { IsFatal: true } fatal ? fatal : null;

    /// <summary>Waits for HELLO, then identifies or resumes. Null if the socket closed.</summary>
    private async Task<TimeSpan?> HandshakeAsync(IDiscordSocket socket, CancellationToken cancellationToken)
    {
        var hello = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (hello is null)
        {
            return null;
        }

        var frame = DiscordGatewayProtocol.ReadFrame(hello)
            ?? throw new InvalidOperationException("Discord's first frame was not a gateway frame.");

        var interval = DiscordGatewayProtocol.ReadHeartbeatInterval(frame)
            ?? throw new InvalidOperationException("HELLO carried no heartbeat interval.");

        var resuming = this.session.CanResume;
        if (!resuming)
        {
            await this.WaitForIdentifySlotAsync(cancellationToken).ConfigureAwait(false);
        }

        var send = resuming
            ? DiscordGatewayProtocol.Resume(
                this.options.Token, this.session.SessionId!, this.session.LastSequence!.Value)
            : DiscordGatewayProtocol.Identify(this.options.Token);

        await socket.SendAsync(send, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Discord gateway {Action}; heartbeat every {Seconds:F0}s",
            resuming ? "resumed" : "identified",
            interval.TotalSeconds);

        return interval;
    }

    /// <summary>Holds off an IDENTIFY until Discord will accept another one.</summary>
    /// <remarks>
    /// Discord allows one identify every five seconds and a thousand a day, and resets the
    /// token of a bot that abuses it. A reconnect loop with no floor spent that budget at
    /// roughly twenty-four a minute on 2026-08-30.
    /// </remarks>
    private async Task WaitForIdentifySlotAsync(CancellationToken cancellationToken)
    {
        var since = this.clock.GetUtcNow() - this.lastIdentify;
        if (since < identifyFloor)
        {
            await Task.Delay(identifyFloor - since, this.clock, cancellationToken).ConfigureAwait(false);
        }

        this.lastIdentify = this.clock.GetUtcNow();
    }

    private async Task HeartbeatAsync(
        IDiscordSocket socket, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval, this.clock);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var beat = DiscordGatewayProtocol.Heartbeat(this.session.LastSequence);
            await socket.SendAsync(beat, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(
        IDiscordSocket socket,
        ChannelWriter<InboundMessage> writer,
        CancellationTokenSource connection,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var raw = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (raw is null)
            {
                return;
            }

            var frame = DiscordGatewayProtocol.ReadFrame(raw);
            if (frame is null)
            {
                continue;
            }

            this.session.Observe(frame);
            if (this.ShouldReconnect(frame))
            {
                return;
            }

            await this.OfferAsync(frame, writer, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether Discord has asked us to go away and come back.</summary>
    private bool ShouldReconnect(GatewayFrame frame)
    {
        if (frame.Opcode == DiscordOpcode.InvalidSession)
        {
            this.logger.LogInformation("Discord invalidated the session; identifying afresh");
            this.session.Invalidate();
            return true;
        }

        return frame.Opcode == DiscordOpcode.Reconnect;
    }

    private async Task OfferAsync(
        GatewayFrame frame, ChannelWriter<InboundMessage> writer, CancellationToken cancellationToken)
    {
        var message = DiscordGatewayProtocol.ReadMessage(frame);
        if (message is null || message.AuthorIsBot)
        {
            return;
        }

        if (this.options.GuildId.Length > 0
            && message.GuildId.Length > 0
            && !string.Equals(message.GuildId, this.options.GuildId, StringComparison.Ordinal))
        {
            return;
        }

        var inbound = new InboundMessage(
            message.AuthorId, message.ChannelId, message.Content, this.clock.GetUtcNow());

        if (ChannelDisclosurePolicy.ShouldAnswer(inbound, this.options.OwnerUserId, this.session.SelfId))
        {
            await writer.WriteAsync(inbound, cancellationToken).ConfigureAwait(false);
        }
    }
}
