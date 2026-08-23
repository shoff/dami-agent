
namespace Dami.Contracts.Events;

/// <summary>One durable, replayable record of something the runtime did.</summary>
/// <remarks>
/// Shape from dami-core-system-architecture.md §9.2. Two departures from the charter,
/// both from D-018: the identifier is a trace rather than a turn, because proactive work
/// has no turn, and every event carries an <see cref="ExecutionOrigin"/>.
///
/// The event store is canonical and OpenTelemetry is an export path (D-017), so this
/// type is the source of truth rather than a projection of a span.
/// </remarks>
public sealed record ExecutionEvent
{
    /// <summary>Creates an event. <paramref name="sequence"/> is assigned by the store.</summary>
    public ExecutionEvent(
        Guid eventId,
        Guid traceId,
        Guid spanId,
        Guid? parentSpanId,
        ExecutionOrigin origin,
        string actorId,
        ExecutionEventType type,
        ExecutionStatus status,
        DateTimeOffset occurredAt,
        string label,
        string? payloadReference = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        long sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(label);

        if (parentSpanId == spanId)
        {
            throw new ArgumentException("A span cannot be its own parent.", nameof(parentSpanId));
        }

        this.EventId = eventId;
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.ParentSpanId = parentSpanId;
        this.Origin = origin;
        this.ActorId = actorId;
        this.Type = type;
        this.Status = status;
        this.OccurredAt = occurredAt;
        this.Label = label;
        this.PayloadReference = payloadReference;
        this.Metadata = metadata;
        this.Sequence = sequence;
    }

    /// <summary>Total order of persistence. Assigned by the store; 0 before it is appended.</summary>
    /// <remarks>
    /// Not the same as <see cref="OccurredAt"/>. Events can be persisted out of the order
    /// they happened, and replay follows this.
    /// </remarks>
    public long Sequence { get; init; }

    /// <summary>Idempotency key. A replayed append conflicts on this and is discarded.</summary>
    public Guid EventId { get; }

    /// <summary>The trace this belongs to. Not necessarily a user turn — see <see cref="Origin"/>.</summary>
    public Guid TraceId { get; }

    /// <summary>The operation this event describes.</summary>
    public Guid SpanId { get; }

    /// <summary>The operation that caused this one, if any. Parent/child edges in the graph.</summary>
    public Guid? ParentSpanId { get; }

    /// <summary>What caused this trace to exist.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>The agent, worker, or service that acted.</summary>
    public string ActorId { get; }

    /// <summary>What kind of operation this is.</summary>
    public ExecutionEventType Type { get; }

    /// <summary>How the operation stands.</summary>
    public ExecutionStatus Status { get; }

    /// <summary>When it happened, as reported by the actor.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Short human-readable description, shown in the CLI tree and the graph node.</summary>
    public string Label { get; }

    /// <summary>
    /// Where the payload lives, if it is too large or too sensitive to inline.
    /// </summary>
    /// <remarks>
    /// A reference rather than the content, so redaction and retention are decided once,
    /// at the store, rather than at every call site.
    /// </remarks>
    public string? PayloadReference { get; }

    /// <summary>Small, non-sensitive facts about the operation.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }
}
