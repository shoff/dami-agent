using System.Security.Cryptography;
using System.Text;
using Dami.Contracts.Sessions;

namespace Dami.Host.Discord;

/// <summary>Gives each Discord channel a durable conversation session.</summary>
/// <remarks>
/// The session id is derived from the channel id rather than stored beside it. A mapping
/// table would need a migration in schema <c>dami</c>, which is shared state another agent
/// owns work in, and it would need to be read before every turn. Deriving it is stable
/// across restarts by construction and needs neither.
///
/// It is a hash, not a secret: a Discord channel id is not sensitive and the derivation is
/// deterministic on purpose.
/// </remarks>
public static class DiscordConversations
{
    /// <summary>The session a channel's conversation lives in.</summary>
    public static Guid SessionFor(string channelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("dami-discord-channel:" + channelId));
        var id = new Guid(hash.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(hash.AsSpan(16, 16)) : id;
    }

    /// <summary>Ensures the session exists, so turns have somewhere to be journalled.</summary>
    public static async Task EnsureAsync(
        IConversationSessionStore sessions,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (await sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        await sessions.CreateAsync(
            new ConversationSession(sessionId, ConversationSessionState.Active, now, now),
            cancellationToken).ConfigureAwait(false);
    }
}
