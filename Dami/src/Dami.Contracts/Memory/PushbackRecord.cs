namespace Dami.Contracts.Memory;

/// <summary>One occasion on which Dami challenged something.</summary>
/// <remarks>
/// D-011. Dami is required both to tune itself on Steve's reactions and to function as
/// an auditor, and those pull in opposite directions: a system optimising on reactions
/// learns that challenge produces negative ones, and the cheapest route to "his criticism
/// lands well" is fewer criticisms. Given six months it agrees with everything, warmly.
///
/// The drift is invisible as tone and visible as a count, which is the entire reason this
/// record exists. It detects; it does not prevent.
/// </remarks>
public sealed record PushbackRecord
{
    /// <summary>Creates a pushback record.</summary>
    public PushbackRecord(
        Guid pushbackId,
        Guid traceId,
        string challenge,
        string challengedAssumption,
        PushbackOutcome outcome,
        DateTimeOffset occurredAt,
        string? followUpNote = null)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(challengedAssumption);

        this.PushbackId = pushbackId;
        this.TraceId = traceId;
        this.Challenge = challenge;
        this.ChallengedAssumption = challengedAssumption;
        this.Outcome = outcome;
        this.OccurredAt = occurredAt;
        this.FollowUpNote = followUpNote;
    }

    /// <summary>Identity.</summary>
    public Guid PushbackId { get; }

    /// <summary>The trace the challenge happened in.</summary>
    public Guid TraceId { get; }

    /// <summary>What Dami said.</summary>
    public string Challenge { get; }

    /// <summary>The assumption it contradicted.</summary>
    public string ChallengedAssumption { get; }

    /// <summary>How it landed.</summary>
    public PushbackOutcome Outcome { get; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>What followed, recorded later.</summary>
    public string? FollowUpNote { get; }
}
