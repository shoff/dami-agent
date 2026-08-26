namespace Dami.Contracts.Privacy;

/// <summary>Steve's correction of one gate decision: what it should have been, and why.</summary>
public sealed record DisclosureCorrection(
    Disclosure Corrected,
    string Note,
    string CorrectedBy,
    DateTimeOffset CorrectedAt);

/// <summary>One gate decision about one item, and the correction to it if Steve made one.</summary>
public sealed record DisclosureDecision(
    Guid DecisionId,
    Guid TraceId,
    string Question,
    string Original,
    Disclosure Disclosure,
    string Sendable,
    string Reason,
    DateTimeOffset DecidedAt,
    DisclosureCorrection? Correction);

/// <summary>
/// The durable record of what the gate decided, so a decision can be reviewed and
/// corrected, and so the gate can read the corrections back as examples of the user's
/// own boundaries.
/// </summary>
public interface IDisclosureLedger
{
    /// <summary>Records every decision of one gated turn.</summary>
    Task RecordAsync(
        Guid traceId,
        string question,
        IReadOnlyList<DisclosedItem> decisions,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken);

    /// <summary>Recent decisions, newest first, each with its correction if any.</summary>
    Task<IReadOnlyList<DisclosureDecision>> RecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Records a correction. False when the decision is unknown or already corrected — a
    /// correction is one statement of the boundary, not a conversation.
    /// </summary>
    Task<bool> CorrectAsync(
        Guid decisionId,
        DisclosureCorrection correction,
        CancellationToken cancellationToken);

    /// <summary>Corrected decisions, newest first — the examples the gate learns from.</summary>
    Task<IReadOnlyList<DisclosureDecision>> CorrectionsAsync(int limit, CancellationToken cancellationToken);
}
