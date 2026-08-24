using System.Collections.Concurrent;

namespace Dami.Core.Sessions;

/// <summary>Process-local cancellation generations for the single authoritative Host.</summary>
public sealed class SessionCancellationRegistry : ISessionCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> sources = new();

    /// <inheritdoc />
    public CancellationToken TokenFor(Guid sessionId)
    {
        EnsureSessionId(sessionId);
        return this.sources.GetOrAdd(
            sessionId, static _ => new CancellationTokenSource()).Token;
    }

    /// <inheritdoc />
    public Task InterruptAsync(Guid sessionId)
    {
        EnsureSessionId(sessionId);
        return this.sources.GetOrAdd(
            sessionId, static _ => new CancellationTokenSource()).CancelAsync();
    }

    /// <inheritdoc />
    public void Resume(Guid sessionId)
    {
        EnsureSessionId(sessionId);
        while (true)
        {
            if (!this.sources.TryGetValue(sessionId, out var current))
            {
                var initial = new CancellationTokenSource();
                if (this.sources.TryAdd(sessionId, initial))
                {
                    return;
                }

                initial.Dispose();
                continue;
            }

            if (!current.IsCancellationRequested)
            {
                return;
            }

            var replacement = new CancellationTokenSource();
            if (this.sources.TryUpdate(sessionId, replacement, current))
            {
                return;
            }

            replacement.Dispose();
        }
    }

    private static void EnsureSessionId(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A session id cannot be empty.", nameof(sessionId));
        }
    }
}
