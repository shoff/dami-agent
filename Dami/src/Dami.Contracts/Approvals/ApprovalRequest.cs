using Dami.Contracts.Events;

namespace Dami.Contracts.Approvals;

/// <summary>One consequential action, blocked until a human resolves it (charter §10.2).</summary>
public sealed record ApprovalRequest
{
    /// <summary>Creates a request.</summary>
    public ApprovalRequest(
        Guid approvalId,
        Guid traceId,
        string requestedBy,
        string action,
        string scope,
        string resource,
        DateTimeOffset requestedAt,
        ApprovalStatus status = ApprovalStatus.Pending,
        DateTimeOffset? resolvedAt = null,
        string? resolvedNote = null,
        DateTimeOffset? expiresAt = null,
        ExecutionOrigin origin = ExecutionOrigin.UserTurn,
        Guid? parentSpanId = null)
    {
        ArgumentNullException.ThrowIfNull(requestedBy);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(resource);
        if (parentSpanId == Guid.Empty || parentSpanId == approvalId)
        {
            throw new ArgumentException(
                "An approval parent span must be non-empty and distinct from the approval id.",
                nameof(parentSpanId));
        }

        this.ApprovalId = approvalId;
        this.TraceId = traceId;
        this.RequestedBy = requestedBy;
        this.Action = action;
        this.Scope = scope;
        this.Resource = resource;
        this.RequestedAt = requestedAt;
        this.Status = status;
        this.ResolvedAt = resolvedAt;
        this.ResolvedNote = resolvedNote;
        this.ExpiresAt = expiresAt;
        this.Origin = origin;
        this.ParentSpanId = parentSpanId;
    }

    /// <summary>Stable identifier.</summary>
    public Guid ApprovalId { get; }

    /// <summary>The trace the request belongs to — approvals are trace nodes, not dialogs.</summary>
    public Guid TraceId { get; }

    /// <summary>Which service or component asked.</summary>
    public string RequestedBy { get; }

    /// <summary>The consequential action, human-readable.</summary>
    public string Action { get; }

    /// <summary>What kind of consequence — "filesystem", "external-write", "financial"…</summary>
    public string Scope { get; }

    /// <summary>The affected resource — a path, an account, a destination.</summary>
    public string Resource { get; }

    /// <summary>When it was requested.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Where it stands.</summary>
    public ApprovalStatus Status { get; }

    /// <summary>When it was resolved, if it has been.</summary>
    public DateTimeOffset? ResolvedAt { get; }

    /// <summary>What the human said, beyond yes or no.</summary>
    public string? ResolvedNote { get; }

    /// <summary>After this, unanswered means denied.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Whether the gated action began in a user turn or background work.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>The originating execution span, when one is known.</summary>
    public Guid? ParentSpanId { get; }
}
