using System.Collections.Concurrent;

namespace Dami.Host.Discord;

/// <summary>The trace of the latest answered turn in each conversation.</summary>
public sealed class DiscordLastTurns
{
    private readonly ConcurrentDictionary<string, Guid> latest = new(StringComparer.Ordinal);

    /// <summary>Notes that <paramref name="traceId"/> answered in <paramref name="conversationId"/>.</summary>
    public void Answered(string conversationId, Guid traceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        this.latest[conversationId] = traceId;
    }

    /// <summary>The latest answered turn there, or null.</summary>
    public Guid? LastIn(string conversationId) =>
        this.latest.TryGetValue(conversationId, out var traceId) ? traceId : null;
}
