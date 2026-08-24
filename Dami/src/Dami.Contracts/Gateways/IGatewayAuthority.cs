namespace Dami.Contracts.Gateways;

/// <summary>Grants the right to be the one authoritative gateway of a given name (M1).</summary>
/// <remarks>
/// The charter forbids a second authoritative Discord gateway during cutover: two bots
/// on one token answer every message twice, and neither process can see the other doing
/// it. Authority is therefore taken, not assumed — a gateway that cannot acquire it must
/// refuse to serve rather than run "probably alone".
/// </remarks>
public interface IGatewayAuthority
{
    /// <summary>
    /// Attempts to become the authoritative holder. Returns null when another process
    /// already holds it — the caller must then not serve.
    /// </summary>
    Task<IGatewayLease?> TryAcquireAsync(string gatewayName, CancellationToken cancellationToken);
}

/// <summary>Held for as long as this process is the authority. Disposing releases it.</summary>
public interface IGatewayLease : IAsyncDisposable
{
    /// <summary>Which gateway this lease is for.</summary>
    string GatewayName { get; }

    /// <summary>Records that the holder is still alive.</summary>
    Task HeartbeatAsync(CancellationToken cancellationToken);
}
