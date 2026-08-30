namespace Dami.Core.Sessions;

/// <summary>Bridges durable session lifecycle changes to active execution tokens.</summary>
public interface ISessionCancellationRegistry
{
    /// <summary>Gets the cancellation generation for new work in a session.</summary>
    CancellationToken TokenFor(Guid sessionId);

    /// <summary>Cancels every execution using the session's current generation.</summary>
    Task InterruptAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Starts a fresh non-cancelled generation after durable resume.</summary>
    void Resume(Guid sessionId);
}
