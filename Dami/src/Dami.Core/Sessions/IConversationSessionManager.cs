using Dami.Contracts.Sessions;

namespace Dami.Core.Sessions;

/// <summary>Application lifecycle boundary for durable conversations.</summary>
public interface IConversationSessionManager
{
    /// <summary>Creates or returns the session with the caller's stable identifier.</summary>
    Task<ConversationSession> StartAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Finds a session.</summary>
    Task<ConversationSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Lists recently active sessions, newest first.</summary>
    IAsyncEnumerable<ConversationSession> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Resumes an interrupted session.</summary>
    Task<ConversationSession?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Interrupts an active session and its running turns.</summary>
    Task<ConversationSession?> InterruptAsync(Guid sessionId, CancellationToken cancellationToken);
}
