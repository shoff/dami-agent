namespace Dami.Contracts.Memory;

/// <summary>Vectors over the ACTIVE believed set only (D-009).</summary>
/// <remarks>
/// Retraction removes the vector at the database layer, atomically — a dead belief must
/// not stay semantically retrievable, and that property does not depend on which code
/// path retracted it.
/// </remarks>
public interface IConclusionEmbeddingStore
{
    /// <summary>Active conclusions that have no vector under the given model, oldest first.</summary>
    IAsyncEnumerable<Conclusion> UnembeddedAsync(
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Stores one vector. Idempotent per conclusion.</summary>
    Task StoreAsync(
        Guid conclusionId,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken);

    /// <summary>The nearest active beliefs to a query vector, by cosine distance.</summary>
    IAsyncEnumerable<(Conclusion Conclusion, double Distance)> NearestAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken);
}
