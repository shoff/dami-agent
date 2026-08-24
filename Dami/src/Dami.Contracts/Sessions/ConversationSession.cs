namespace Dami.Contracts.Sessions;

/// <summary>A durable multi-turn conversation boundary.</summary>
public sealed record ConversationSession
{
    /// <summary>Creates a session.</summary>
    public ConversationSession(
        Guid sessionId,
        ConversationSessionState state,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A session id cannot be empty.", nameof(sessionId));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown session state.");
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt), updatedAt, "A session cannot be updated before it was created.");
        }

        this.SessionId = sessionId;
        this.State = state;
        this.CreatedAt = createdAt;
        this.UpdatedAt = updatedAt;
    }

    /// <summary>Stable conversation identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Current lifecycle state.</summary>
    public ConversationSessionState State { get; }

    /// <summary>When the session was started.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When its durable state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; }
}
