namespace Dami.Contracts.Models;

/// <summary>Local text embedding — the first piece of the model layer.</summary>
/// <remarks>
/// Implementations talk to a localhost sidecar (TEI on this workstation). That is local
/// inference, not egress: nothing embedded here leaves the host, which is exactly why
/// the taste model may see personal interests while the fetch that follows may not
/// (D-012: the profile stays in, the queries go out).
/// </remarks>
public interface IEmbeddingClient
{
    /// <summary>Embeds each text, preserving order.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
