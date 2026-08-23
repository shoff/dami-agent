namespace Dami.Contracts.Models;

/// <summary>Local text generation — the sidecar half of §7.4's model routing.</summary>
/// <remarks>
/// Like <see cref="IEmbeddingClient"/>, implementations talk to loopback. Anything
/// passed through this may be profile-derived and stays on the host; the frontier-model
/// path, when it exists, is an egress event and lives behind a different door.
/// </remarks>
public interface IChatClient
{
    /// <summary>Completes a prompt and returns the final text.</summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}
