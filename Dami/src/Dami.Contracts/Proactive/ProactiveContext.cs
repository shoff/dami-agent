namespace Dami.Contracts.Proactive;

/// <summary>What a proactive pass is told when it runs.</summary>
public sealed record ProactiveContext
{
    /// <summary>Creates a context.</summary>
    public ProactiveContext(Guid traceId, DateTimeOffset scheduledAt, DateTimeOffset? lastRanAt)
    {
        this.TraceId = traceId;
        this.ScheduledAt = scheduledAt;
        this.LastRanAt = lastRanAt;
    }

    /// <summary>The trace this pass runs under. Every event it emits carries it.</summary>
    public Guid TraceId { get; }

    /// <summary>When this run was due.</summary>
    public DateTimeOffset ScheduledAt { get; }

    /// <summary>When the service last completed, so a pass can scope itself to what is new.</summary>
    public DateTimeOffset? LastRanAt { get; }
}
