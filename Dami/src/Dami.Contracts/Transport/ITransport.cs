namespace Dami.Contracts.Transport;

/// <summary>Provides serialization-independent frame delivery.</summary>
public interface ITransport
{
    /// <summary>Sends one frame.</summary>
    ValueTask SendAsync(
        TransportFrame frame,
        CancellationToken cancellationToken);

    /// <summary>Receives frames until the transport completes or cancellation is requested.</summary>
    IAsyncEnumerable<TransportFrame> ReceiveAsync(
        CancellationToken cancellationToken);
}
