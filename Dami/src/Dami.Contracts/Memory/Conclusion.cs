namespace Dami.Contracts.Memory;

/// <summary>Something Dami believes about Steve.</summary>
/// <remarks>
/// Shape from dami-core-system-architecture.md §9.2. Conclusions are inferences and get
/// retracted, which is why they are relational and supersedable rather than living in
/// the append-only corpus alongside observations (D-009). A retracted conclusion left in
/// a vector index stays semantically retrievable forever, because nearest-neighbour
/// search does not respect tombstones unless it is made to.
/// </remarks>
public sealed record Conclusion
{
    /// <summary>Creates a conclusion.</summary>
    public Conclusion(
        Guid conclusionId,
        Guid? supersedesId,
        string subject,
        string statement,
        double confidence,
        ConclusionSource source,
        DateTimeOffset concludedAt,
        IReadOnlyList<Guid>? supportingObservations = null,
        DateTimeOffset? retractedAt = null,
        string? retractionReason = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(statement);

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence), confidence, "Confidence is a probability in [0, 1].");
        }

        if (supersedesId == conclusionId)
        {
            throw new ArgumentException("A conclusion cannot supersede itself.", nameof(supersedesId));
        }

        if (retractedAt is not null && string.IsNullOrWhiteSpace(retractionReason))
        {
            throw new ArgumentException(
                "A retracted conclusion carries the reason it was retracted.", nameof(retractionReason));
        }

        this.ConclusionId = conclusionId;
        this.SupersedesId = supersedesId;
        this.Subject = subject;
        this.Statement = statement;
        this.Confidence = confidence;
        this.Source = source;
        this.ConcludedAt = concludedAt;
        this.SupportingObservations = supportingObservations ?? Array.Empty<Guid>();
        this.RetractedAt = retractedAt;
        this.RetractionReason = retractionReason;
    }

    /// <summary>Identity.</summary>
    public Guid ConclusionId { get; }

    /// <summary>The conclusion this replaces, if it is a correction.</summary>
    /// <remarks>Following this chain backwards is the audit trail for a belief.</remarks>
    public Guid? SupersedesId { get; }

    /// <summary>What the conclusion is about.</summary>
    public string Subject { get; }

    /// <summary>The belief, in plain language, as it would be shown to Steve.</summary>
    public string Statement { get; }

    /// <summary>How strongly it is held, in [0, 1].</summary>
    public double Confidence { get; }

    /// <summary>Where it came from.</summary>
    public ConclusionSource Source { get; }

    /// <summary>When it was concluded.</summary>
    public DateTimeOffset ConcludedAt { get; }

    /// <summary>The observations that support it.</summary>
    /// <remarks>
    /// A conclusion with none is an assertion rather than an inference, and the audit
    /// view should say so.
    /// </remarks>
    public IReadOnlyList<Guid> SupportingObservations { get; }

    /// <summary>When it stopped being believed, if it has.</summary>
    public DateTimeOffset? RetractedAt { get; }

    /// <summary>Why it was retracted.</summary>
    public string? RetractionReason { get; }

    /// <summary>Whether this is part of the currently believed set.</summary>
    public bool IsActive => this.RetractedAt is null;
}
