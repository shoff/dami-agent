namespace Dami.Gateway.Discord;

/// <summary>Why Discord closed the socket.</summary>
public sealed record DiscordClose(int Code, string Description)
{
    /// <summary>
    /// Whether retrying can never succeed. Reconnecting against these burns the identify
    /// budget for nothing, and Discord resets a token that abuses it.
    /// </summary>
    /// <remarks>
    /// 4004 authentication failed, 4010 invalid shard, 4011 sharding required, 4012 invalid
    /// API version, 4013 invalid intents, 4014 disallowed (privileged) intents.
    /// </remarks>
    public bool IsFatal => this.Code is 4004 or 4010 or 4011 or 4012 or 4013 or 4014;

    /// <summary>What a human should do about it.</summary>
    public string Advice => this.Code switch
    {
        4004 => "The bot token is wrong or has been reset. Replace Discord__Token in /etc/dami/discord.env.",
        4013 or 4014 =>
            "Discord refused the requested intents. Enable MESSAGE CONTENT under Bot > Privileged "
            + "Gateway Intents in the developer portal.",
        _ => "This connection can never succeed; the gateway has stopped rather than retry.",
    };
}

/// <summary>The gateway socket, behind a seam so the connection loop can be tested.</summary>
/// <remarks>
/// The loop above this — identify, heartbeat, resume, reconnect — is where the protocol
/// bugs live, and none of it should require a real connection to Discord to exercise.
/// ADR-0024 also names this seam as the reversal path: a Discord.Net implementation
/// would slot in here without disturbing anything above.
/// </remarks>
public interface IDiscordSocket : IAsyncDisposable
{
    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(Uri gateway, CancellationToken cancellationToken);

    /// <summary>Sends one frame.</summary>
    Task SendAsync(string json, CancellationToken cancellationToken);

    /// <summary>Receives one whole frame, or null when the peer closed the connection.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Why the peer closed, once <see cref="ReceiveAsync"/> has returned null. Discord
    /// reports fatal conditions here and nowhere else — 4004 is a bad token, 4014 is a
    /// privileged intent that was never enabled in the developer portal — so discarding
    /// it makes a permanent failure look exactly like a dropped connection.
    /// </summary>
    DiscordClose? CloseReason { get; }
}

/// <summary>Posts messages back to Discord over its REST API.</summary>
/// <remarks>
/// Separate from the socket because Discord is: the gateway pushes events but replies go
/// over HTTP. Behind a seam for the same reason — a test should be able to assert what
/// was said without a network.
/// </remarks>
public interface IDiscordRest
{
    /// <summary>Posts a message to a channel.</summary>
    Task PostMessageAsync(string channelId, string text, CancellationToken cancellationToken);
}
