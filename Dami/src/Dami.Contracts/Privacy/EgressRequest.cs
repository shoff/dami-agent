namespace Dami.Contracts.Privacy;

/// <summary>A request to fetch something from outside the host.</summary>
/// <remarks>
/// Deliberately not an <c>HttpRequestMessage</c>: the abstraction layers must not name
/// the mechanism, and more importantly the shape constrains what can leave. There is a
/// destination and a purpose — no body, no arbitrary headers. D-012: queries go out,
/// the profile stays in, and the narrowness of this type is part of the enforcement.
/// </remarks>
public sealed record EgressRequest
{
    /// <summary>Creates an egress request.</summary>
    public EgressRequest(Uri destination, string purpose, Guid traceId, Events.ExecutionOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(purpose);

        if (!destination.IsAbsoluteUri)
        {
            throw new ArgumentException("An egress destination is an absolute URI.", nameof(destination));
        }

        this.Destination = destination;
        this.Purpose = purpose;
        this.TraceId = traceId;
        this.Origin = origin;
    }

    /// <summary>Where the request goes.</summary>
    public Uri Destination { get; }

    /// <summary>Why, in one human-readable line. Appears in the egress event.</summary>
    public string Purpose { get; }

    /// <summary>The trace this egress belongs to, so it appears in the caller's graph.</summary>
    public Guid TraceId { get; }

    /// <summary>What kind of work is reaching out.</summary>
    public Events.ExecutionOrigin Origin { get; }
}
