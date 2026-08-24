using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Contracts.Privacy;

/// <summary>Privacy and trace provenance for one body-capable egress operation.</summary>
public sealed class EgressOperationContext
{
    /// <summary>Creates an egress operation context.</summary>
    public EgressOperationContext(
        string purpose,
        PrivacyClass privacy,
        Guid traceId,
        Guid parentSpanId,
        ExecutionOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (purpose.Length > 160 || purpose.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "Egress purpose must be a single line of at most 160 characters.", nameof(purpose));
        }

        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy));
        }

        if (traceId == Guid.Empty)
        {
            throw new ArgumentException("Egress requires a trace identifier.", nameof(traceId));
        }

        if (parentSpanId == Guid.Empty)
        {
            throw new ArgumentException("Egress requires a parent span identifier.", nameof(parentSpanId));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        this.Purpose = purpose;
        this.Privacy = privacy;
        this.TraceId = traceId;
        this.ParentSpanId = parentSpanId;
        this.Origin = origin;
    }

    /// <summary>Gets the safe human-readable purpose recorded in event labels.</summary>
    public string Purpose { get; }

    /// <summary>Gets the classification enforced before a body can leave.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>Gets the caller trace receiving egress events.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the caller span parenting egress events.</summary>
    public Guid ParentSpanId { get; }

    /// <summary>Gets the origin of the enclosing work.</summary>
    public ExecutionOrigin Origin { get; }
}
