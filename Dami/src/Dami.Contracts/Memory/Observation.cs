namespace Dami.Contracts.Memory;

/// <summary>Something that happened, recorded verbatim and never revised.</summary>
/// <remarks>
/// The other half of D-009. Observations are not beliefs: they record what was said,
/// committed, or measured, and they are never wrong because the record is the record.
/// That is why they are append-only and conclusions are not — mixing the two means a
/// retracted belief stays semantically retrievable forever.
/// </remarks>
public sealed record Observation
{
    /// <summary>Creates an observation.</summary>
    public Observation(
        Guid observationId,
        DateTimeOffset occurredAt,
        string source,
        string body,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? recordedAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);

        this.ObservationId = observationId;
        this.OccurredAt = occurredAt;
        this.Source = source;
        this.Body = body;
        this.Metadata = metadata;
        this.RecordedAt = recordedAt;
    }

    /// <summary>Identity, and the idempotency key for recording.</summary>
    public Guid ObservationId { get; }

    /// <summary>When the thing happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>When the corpus learned of it.</summary>
    /// <remarks>
    /// Assigned by the store; null on an observation that has not been recorded yet. It
    /// differs from <see cref="OccurredAt"/> whenever something is backfilled, and the
    /// gap between them is itself worth knowing.
    /// </remarks>
    public DateTimeOffset? RecordedAt { get; }

    /// <summary>Where it came from — a gateway, a collector, a domain service.</summary>
    public string Source { get; }

    /// <summary>What happened, in the words it happened in.</summary>
    public string Body { get; }

    /// <summary>Small structured facts about the observation.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }
}
