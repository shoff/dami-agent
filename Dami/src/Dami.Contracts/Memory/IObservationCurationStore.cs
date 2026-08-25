namespace Dami.Contracts.Memory;

/// <summary>Storage for curated observation text (derived, never overwriting the source).</summary>
public interface IObservationCurationStore
{
    /// <summary>Observations still written in imported transcript voice, oldest first.</summary>
    IAsyncEnumerable<Observation> UncuratedAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores a rewrite. Idempotent per observation.</summary>
    Task CurateAsync(
        Guid observationId,
        string curatedBody,
        CancellationToken cancellationToken);
}
