namespace Dami.Contracts.Memory;

/// <summary>Vectors over the corpus — derived data, rebuildable at any time.</summary>
public interface IObservationEmbeddingStore
{
    /// <summary>Observations that have no vector under the given model, oldest first.</summary>
    IAsyncEnumerable<Observation> UnembeddedAsync(
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Stores one vector. Idempotent per observation.</summary>
    Task StoreAsync(
        Guid observationId,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken);

    /// <summary>The nearest observations to a query vector, by cosine distance.</summary>
    IAsyncEnumerable<(Observation Observation, double Distance)> NearestAsync(
        float[] queryEmbedding,
        int limit,
        CancellationToken cancellationToken);
}
