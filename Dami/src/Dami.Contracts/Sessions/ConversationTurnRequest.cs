namespace Dami.Contracts.Sessions;

/// <summary>An idempotent request to add one turn to a session.</summary>
public sealed record ConversationTurnRequest
{
    /// <summary>Creates a request.</summary>
    public ConversationTurnRequest(
        Guid sessionId,
        Guid requestId,
        string message,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (sessionId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new ArgumentException("Session and request ids must be non-empty.");
        }

        this.SessionId = sessionId;
        this.RequestId = requestId;
        this.Message = message;
        this.RequestedAt = requestedAt;
    }

    /// <summary>The conversation receiving the turn.</summary>
    public Guid SessionId { get; }

    /// <summary>Client retry key, unique within the session.</summary>
    public Guid RequestId { get; }

    /// <summary>The user's byte-exact message.</summary>
    public string Message { get; }

    /// <summary>Candidate start time; an existing reservation retains its original value.</summary>
    public DateTimeOffset RequestedAt { get; }
}
