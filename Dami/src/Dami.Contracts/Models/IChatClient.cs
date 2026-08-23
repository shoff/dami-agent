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

    /// <summary>Streams a completion as text fragments, in order (C-04: streams, not callbacks).</summary>
    /// <remarks>Thinking-mode content is not yielded; only answer text arrives here.</remarks>
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken);
}
