namespace Dami.Contracts.Transport;

/// <summary>Provides serialization-independent frame delivery.</summary>
public interface ITransport
{
    /// <summary>Sends one frame, snapshotting its payload before successful completion.</summary>
    /// <remarks>Overlapping calls are supported.</remarks>
    ValueTask SendAsync(
        TransportFrame frame,
        CancellationToken cancellationToken);

    /// <summary>Receives frames until the transport completes or cancellation is requested.</summary>
    /// <remarks>Only one active enumeration is permitted per transport instance.</remarks>
    IAsyncEnumerable<TransportFrame> ReceiveAsync(
        CancellationToken cancellationToken);
}
