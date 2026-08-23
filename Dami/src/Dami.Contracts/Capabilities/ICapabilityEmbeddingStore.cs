namespace Dami.Contracts.Capabilities;

/// <summary>Derived semantic vectors over source-neutral capability descriptions.</summary>
public interface ICapabilityEmbeddingStore
{
    /// <summary>Inserts or replaces one capability version's vector for an embedding model.</summary>
    Task UpsertAsync(
        Guid capabilityId,
        string capabilityVersion,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken);

    /// <summary>Nearest capability identities for one embedding model, by cosine distance.</summary>
    IAsyncEnumerable<(Guid CapabilityId, double Distance)> NearestAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken);
}
