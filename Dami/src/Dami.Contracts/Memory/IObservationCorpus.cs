namespace Dami.Contracts.Memory;

/// <summary>The append-only record of what happened.</summary>
/// <remarks>
/// No update and no delete, and the database enforces that independently. An
/// <see cref="Observation"/> that turns out to be mistaken is not corrected here — a
/// later observation records the correction, and a <see cref="Conclusion"/> drawn from
/// the first is superseded. The corpus is history, and history does not get edited.
/// </remarks>
public interface IObservationCorpus
{
    /// <summary>
    /// Records an observation. Idempotent on <see cref="Observation.ObservationId"/>.
    /// </summary>
    /// <remarks>
    /// A repeat with the same id is discarded rather than applied, so a retrying
    /// collector cannot rewrite what it already wrote.
    /// </remarks>
    Task RecordAsync(Observation observation, CancellationToken cancellationToken);

    /// <summary>Reads one observation.</summary>
    Task<Observation?> FindAsync(Guid observationId, CancellationToken cancellationToken);

    /// <summary>Observations that happened in a half-open window, oldest first.</summary>
    IAsyncEnumerable<Observation> BetweenAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Observations from one source, newest first.</summary>
    IAsyncEnumerable<Observation> FromSourceAsync(
        string source,
        int limit,
        CancellationToken cancellationToken);
}
