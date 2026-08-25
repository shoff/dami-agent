namespace Dami.Contracts.Briefs;

/// <summary>A redacted, review-ready prompt awaiting (or granted) consent to egress (C4).</summary>
public sealed record EgressBrief
{
    /// <summary>Creates a brief.</summary>
    public EgressBrief(
        Guid briefId,
        Guid? approvalId,
        Guid traceId,
        string question,
        string brief,
        string briefSha256,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt = null,
        string? answer = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(briefSha256);

        this.BriefId = briefId;
        this.ApprovalId = approvalId;
        this.TraceId = traceId;
        this.Question = question;
        this.Brief = brief;
        this.BriefSha256 = briefSha256;
        this.CreatedAt = createdAt;
        this.SentAt = sentAt;
        this.Answer = answer;
    }

    /// <summary>Identity.</summary>
    public Guid BriefId { get; }

    /// <summary>
    /// The approval gating this brief, or null when it egressed under standing consent
    /// (an augmented frontier turn). Absent means unapproved-per-turn, never unrecorded.
    /// </summary>
    public Guid? ApprovalId { get; }

    /// <summary>The trace the whole exchange belongs to.</summary>
    public Guid TraceId { get; }

    /// <summary>Steve's original question — LocalOnly, never sent.</summary>
    public string Question { get; }

    /// <summary>The exact bytes that will egress if approved.</summary>
    public string Brief { get; }

    /// <summary>SHA-256 of <see cref="Brief"/> at review time; the executor re-verifies it.</summary>
    public string BriefSha256 { get; }

    /// <summary>When the brief was drafted.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it actually egressed, if it has.</summary>
    public DateTimeOffset? SentAt { get; }

    /// <summary>What the frontier said, recorded next to what was sent.</summary>
    public string? Answer { get; }
}
