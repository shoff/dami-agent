namespace Dami.Contracts.Capabilities;

/// <summary>A capability invocation with the durable trace provenance of its execution.</summary>
public sealed class CapabilityExecutionRequest
{
    /// <summary>Creates a trace-aware execution request.</summary>
    public CapabilityExecutionRequest(
        Guid traceId,
        Guid spanId,
        CapabilityInvocation invocation)
    {
        if (traceId == Guid.Empty)
        {
            throw new ArgumentException("Capability execution requires a trace identifier.", nameof(traceId));
        }

        if (spanId == Guid.Empty)
        {
            throw new ArgumentException("Capability execution requires a span identifier.", nameof(spanId));
        }

        ArgumentNullException.ThrowIfNull(invocation);
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.Invocation = invocation;
    }

    /// <summary>Gets the durable trace containing this execution.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the durable tool span containing this execution.</summary>
    public Guid SpanId { get; }

    /// <summary>Gets the provider-neutral capability invocation.</summary>
    public CapabilityInvocation Invocation { get; }
}
