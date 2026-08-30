using Dami.Contracts.Sessions;

namespace Dami.Core.Sessions;

/// <summary>Coordinates idempotent conversation-session lifecycle operations.</summary>
public sealed class ConversationSessionManager : IConversationSessionManager
{
    private readonly TimeProvider clock;
    private readonly ISessionCancellationRegistry cancellationRegistry;
    private readonly IConversationSessionStore sessionStore;

    /// <summary>Creates the manager.</summary>
    public ConversationSessionManager(
        IConversationSessionStore sessionStore,
        ISessionCancellationRegistry cancellationRegistry,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(cancellationRegistry);
        ArgumentNullException.ThrowIfNull(clock);
        this.sessionStore = sessionStore;
        this.cancellationRegistry = cancellationRegistry;
        this.clock = clock;
    }

    /// <summary>Creates or returns the session with the caller's stable identifier.</summary>
    public async Task<ConversationSession> StartAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A session id cannot be empty.", nameof(sessionId));
        }

        var existing = await this.sessionStore
            .FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var now = this.clock.GetUtcNow();
        var created = new ConversationSession(
            sessionId, ConversationSessionState.Active, now, now);
        return await this.CreateOrConvergeAsync(created, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resumes an interrupted session, or returns its current/unknown state.</summary>
    public async Task<ConversationSession?> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await this.TransitionAsync(
            sessionId, ConversationSessionState.Interrupted,
            ConversationSessionState.Active, cancellationToken).ConfigureAwait(false);
        if (session?.State == ConversationSessionState.Active)
        {
            this.cancellationRegistry.Resume(sessionId);
        }

        return session;
    }

    /// <summary>Interrupts an active session and its running turns.</summary>
    public async Task<ConversationSession?> InterruptAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await this.TransitionAsync(
            sessionId, ConversationSessionState.Active,
            ConversationSessionState.Interrupted, cancellationToken).ConfigureAwait(false);
        if (session?.State == ConversationSessionState.Interrupted)
        {
            await this.cancellationRegistry.InterruptAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        return session;
    }

    /// <inheritdoc />
    public Task<ConversationSession?> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return this.sessionStore.FindAsync(sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ConversationSession> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        return this.sessionStore.ListRecentAsync(limit, cancellationToken);
    }

    private async Task<ConversationSession?> TransitionAsync(
        Guid sessionId,
        ConversationSessionState expected,
        ConversationSessionState next,
        CancellationToken cancellationToken)
    {
        var current = await this.sessionStore
            .FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (current is null || current.State == next)
        {
            return current;
        }

        await this.sessionStore.TryTransitionAsync(
            sessionId, expected, next, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return await this.sessionStore
            .FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationSession> CreateOrConvergeAsync(
        ConversationSession created,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.sessionStore.CreateAsync(created, cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (InvalidOperationException)
        {
            // Concurrent starts with the same client id converge on the winner.
            var existing = await this.sessionStore
                .FindAsync(created.SessionId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }
}
