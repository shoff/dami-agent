namespace Dami.Contracts.Sessions;

/// <summary>Durable conversation-session lifecycle storage.</summary>
public interface IConversationSessionStore
{
    /// <summary>Creates a session; an exact retry is idempotent.</summary>
    Task CreateAsync(ConversationSession session, CancellationToken cancellationToken);

    /// <summary>Finds a session by its stable identifier.</summary>
    Task<ConversationSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Lists the most recently active sessions, newest first.</summary>
    IAsyncEnumerable<ConversationSession> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Changes one expected lifecycle state, returning false for a stale caller.</summary>
    Task<bool> TryTransitionAsync(
        Guid sessionId,
        ConversationSessionState expected,
        ConversationSessionState next,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}
