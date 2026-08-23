namespace Dami.Contracts.Transport;

/// <summary>Creates independently owned transport connections.</summary>
public interface ITransportConnector
{
    /// <summary>Establishes a fresh transport with a new connection-scoped sequence lifetime.</summary>
    ValueTask<ITransport> ConnectAsync(CancellationToken cancellationToken);
}
