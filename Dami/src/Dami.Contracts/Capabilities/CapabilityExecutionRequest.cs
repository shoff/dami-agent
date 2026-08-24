using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Contracts.Capabilities;

/// <summary>A capability invocation with the durable trace provenance of its execution.</summary>
public sealed class CapabilityExecutionRequest
{
    /// <summary>Creates a trace-aware execution request.</summary>
    public CapabilityExecutionRequest(
        Guid traceId,
        Guid spanId,
        PrivacyClass privacy,
        ExecutionOrigin origin,
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

        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        ArgumentNullException.ThrowIfNull(invocation);
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.Privacy = privacy;
        this.Origin = origin;
        this.Invocation = invocation;
    }

    /// <summary>Gets the durable trace containing this execution.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the durable tool span containing this execution.</summary>
    public Guid SpanId { get; }

    /// <summary>Gets the privacy classification governing execution side effects.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>Gets the origin of the trace performing execution.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>Gets the provider-neutral capability invocation.</summary>
    public CapabilityInvocation Invocation { get; }
}
