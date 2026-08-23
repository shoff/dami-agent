namespace Dami.Contracts.Models;

/// <summary>Local cross-encoder reranking — §9.3's second stage.</summary>
/// <remarks>Loopback like the other model clients; candidate text may be personal.</remarks>
public interface IRerankClient
{
    /// <summary>Indices into <paramref name="candidates"/>, best first.</summary>
    Task<IReadOnlyList<int>> RankAsync(
        string query,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken);
}
