namespace Dami.Contracts.Domains;

/// <summary>One structured health fact, derived from an observation (K2, D-007).</summary>
/// <remarks>
/// LocalOnly by nature and by construction: health is the most sensitive domain in the
/// system, and these rows have no egress path anywhere. The provenance link is not
/// decoration — a health fact the model extracted wrong must be traceable to the exact
/// observation it came from, and correctable there.
/// </remarks>
public sealed record HealthEvent
{
    /// <summary>Creates a health event.</summary>
    public HealthEvent(
        Guid healthEventId,
        Guid observationId,
        DateOnly eventDate,
        HealthCategory category,
        string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A health event needs a description.", nameof(description));
        }

        this.HealthEventId = healthEventId;
        this.ObservationId = observationId;
        this.EventDate = eventDate;
        this.Category = category;
        this.Description = description;
    }

    /// <summary>Identity.</summary>
    public Guid HealthEventId { get; }

    /// <summary>The observation this was extracted from — the correction anchor.</summary>
    public Guid ObservationId { get; }

    /// <summary>When the event happened (not when it was extracted).</summary>
    public DateOnly EventDate { get; }

    /// <summary>What kind of health fact this is.</summary>
    public HealthCategory Category { get; }

    /// <summary>The fact itself, in plain language.</summary>
    public string Description { get; }
}

/// <summary>The kinds of health fact the collector recognizes.</summary>
public enum HealthCategory
{
    /// <summary>A condition or diagnosis.</summary>
    Diagnosis,

    /// <summary>A scheduled or attended appointment.</summary>
    Appointment,

    /// <summary>A medication, dose, or change.</summary>
    Medication,

    /// <summary>A measured value — BP, weight, a lab result.</summary>
    Vital,

    /// <summary>A procedure or surgery.</summary>
    Procedure,

    /// <summary>A reported symptom.</summary>
    Symptom,
}
