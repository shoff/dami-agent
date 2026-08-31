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
