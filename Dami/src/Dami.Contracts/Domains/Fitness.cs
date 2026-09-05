namespace Dami.Contracts.Domains;

/// <summary>One cardio session with the metrics the machine or logger captured.</summary>
public sealed record FitnessCardioSession(
    Guid FitnessEventId,
    DateTimeOffset OccurredAt,
    string Modality,
    int? DurationSeconds,
    decimal? DistanceMi,
    int? Calories,
    int? HrAvg,
    int? HrMax,
    bool IsPr,
    string? Notes);

/// <summary>One resistance set, joined to the lift it belongs to.</summary>
public sealed record FitnessSet(
    Guid SetId,
    Guid FitnessEventId,
    DateTimeOffset OccurredAt,
    string Exercise,
    string? MuscleGroup,
    short SetNumber,
    short? Reps,
    decimal? WeightLbs,
    short? Rpe,
    bool IsWarmup);

/// <summary>One body-weight reading.</summary>
public sealed record FitnessWeighIn(DateTimeOffset OccurredAt, decimal WeightLbs);

/// <summary>Everything the fitness domain holds, each list oldest first.</summary>
/// <remarks>
/// The whole domain in one read, deliberately. At a few hundred events the payload is
/// trivial, and a client that holds all of it can recompute any view — a different
/// exercise, a different window — without another round trip, which is what makes a
/// dashboard feel interactive rather than paginated.
/// </remarks>
public sealed record FitnessSnapshot(
    IReadOnlyList<FitnessCardioSession> Cardio,
    IReadOnlyList<FitnessSet> Sets,
    IReadOnlyList<FitnessWeighIn> WeighIns);

/// <summary>One resistance session to record: an exercise and its sets, as Steve reported it.</summary>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Exercise">The exercise or machine name, e.g. "biceps curl (Hammer Strength)".</param>
/// <param name="Sets">Reps, weight and RPE per set, in order.</param>
/// <param name="Source">One of the schema's sources: claude_chat, manual_entry, gym_machine, other.</param>
/// <param name="MuscleGroup">Primary muscle group, if known.</param>
/// <param name="Equipment">One of barbell, dumbbell, cable, machine, bodyweight, kettlebell, band, other; or null.</param>
/// <param name="Notes">Anything else he said.</param>
public sealed record FitnessResistanceEntry(
    DateTimeOffset OccurredAt,
    string Exercise,
    IReadOnlyList<FitnessSetEntry> Sets,
    string Source,
    string? MuscleGroup = null,
    string? Equipment = null,
    string? Notes = null);

/// <summary>One set within a resistance entry.</summary>
public sealed record FitnessSetEntry(short? Reps, decimal? WeightLbs, short? Rpe, bool IsWarmup = false);

/// <summary>One cardio session to record, as the machine or Steve reported it.</summary>
public sealed record FitnessCardioEntry(
    DateTimeOffset OccurredAt,
    string Modality,
    string Source,
    int? DurationSeconds = null,
    decimal? DistanceMi = null,
    int? Calories = null,
    decimal? SpeedMph = null,
    decimal? InclinePct = null,
    int? HrAvg = null,
    int? HrMax = null,
    string? Notes = null);

