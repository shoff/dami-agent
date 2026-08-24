using Dami.Contracts.Events;

namespace Dami.Contracts.Capabilities;

/// <summary>An idempotent, trace-owned, version-pinned skill lifecycle request.</summary>
public sealed record SkillChangeRequest
{
    /// <summary>Creates and validates one skill lifecycle request.</summary>
    public SkillChangeRequest(
        Guid changeId,
        Guid traceId,
        Guid spanId,
        Guid? parentSpanId,
        ExecutionOrigin origin,
        SkillChangeKind kind,
        Guid skillId,
        string? expectedVersion,
        SkillDocument? replacement)
    {
        ValidateIdentifiers(changeId, traceId, spanId, parentSpanId, skillId);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ValidateTransition(kind, skillId, expectedVersion, replacement);
        this.ChangeId = changeId;
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.ParentSpanId = parentSpanId;
        this.Origin = origin;
        this.Kind = kind;
        this.SkillId = skillId;
        this.ExpectedVersion = expectedVersion;
        this.Replacement = replacement;
    }

    /// <summary>Gets the retry-stable mutation identifier.</summary>
    public Guid ChangeId { get; }

    /// <summary>Gets the owning execution trace.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the skill-change span.</summary>
    public Guid SpanId { get; }

    /// <summary>Gets the operation that requested this change.</summary>
    public Guid? ParentSpanId { get; }

    /// <summary>Gets what caused the owning trace.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>Gets the lifecycle transition.</summary>
    public SkillChangeKind Kind { get; }

    /// <summary>Gets the stable target skill identifier.</summary>
    public Guid SkillId { get; }

    /// <summary>Gets the required preimage version for revise and retire.</summary>
    public string? ExpectedVersion { get; }

    /// <summary>Gets the authored/revised document, or null for retirement.</summary>
    public SkillDocument? Replacement { get; }

    private static void ValidateIdentifiers(
        Guid changeId,
        Guid traceId,
        Guid spanId,
        Guid? parentSpanId,
        Guid skillId)
    {
        if (changeId == Guid.Empty || traceId == Guid.Empty || spanId == Guid.Empty
            || skillId == Guid.Empty)
        {
            throw new ArgumentException("Skill changes require non-empty identifiers.");
        }

        if (parentSpanId == spanId)
        {
            throw new ArgumentException("A skill-change span cannot parent itself.", nameof(parentSpanId));
        }
    }

    private static void ValidateTransition(
        SkillChangeKind kind,
        Guid skillId,
        string? expectedVersion,
        SkillDocument? replacement)
    {
        if (kind == SkillChangeKind.Author)
        {
            if (expectedVersion is not null)
            {
                throw new ArgumentException("Authoring cannot name a preimage version.", nameof(expectedVersion));
            }
        }
        else if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new ArgumentException("Revise and retire require a preimage version.", nameof(expectedVersion));
        }

        bool requiresReplacement = kind is SkillChangeKind.Author or SkillChangeKind.Revise;
        if (requiresReplacement != (replacement is not null)
            || (replacement is not null && replacement.SkillId != skillId))
        {
            throw new ArgumentException(
                "The replacement must match author/revise target identity and be absent for retire.",
                nameof(replacement));
        }
    }
}
