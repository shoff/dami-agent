using Dami.Contracts.Events;

namespace Dami.Contracts.ToolStaging;

/// <summary>A retry-stable, trace-owned request to stage one inert tool artifact.</summary>
public sealed record ToolProposalRequest
{
    /// <summary>Creates one proposal request.</summary>
    public ToolProposalRequest(
        Guid proposalId,
        Guid traceId,
        Guid spanId,
        Guid? parentSpanId,
        ExecutionOrigin origin,
        ToolProposalArtifact artifact)
    {
        ValidateIdentifiers(proposalId, traceId, spanId, parentSpanId);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        ArgumentNullException.ThrowIfNull(artifact);
        this.ProposalId = proposalId;
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.ParentSpanId = parentSpanId;
        this.Origin = origin;
        this.Artifact = artifact;
    }

    /// <summary>Gets the retry-stable proposal identifier.</summary>
    public Guid ProposalId { get; }

    /// <summary>Gets the owning trace.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the proposal span.</summary>
    public Guid SpanId { get; }

    /// <summary>Gets the operation that caused this proposal.</summary>
    public Guid? ParentSpanId { get; }

    /// <summary>Gets what caused the owning trace.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>Gets the complete inert review artifact.</summary>
    public ToolProposalArtifact Artifact { get; }

    private static void ValidateIdentifiers(
        Guid proposalId,
        Guid traceId,
        Guid spanId,
        Guid? parentSpanId)
    {
        if (proposalId == Guid.Empty || traceId == Guid.Empty || spanId == Guid.Empty)
        {
            throw new ArgumentException("Tool proposals require non-empty identifiers.");
        }

        if (parentSpanId == Guid.Empty || parentSpanId == spanId)
        {
            throw new ArgumentException(
                "A proposal parent span must be non-empty and distinct from its span.",
                nameof(parentSpanId));
        }
    }
}
