using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Dami.Contracts.Transport;
using Dami.Transport.Framing;

namespace Dami.Transport.Tests;

public sealed class TcpTransportConnectorTests
{
    [Fact]
    public async Task ConnectAsync_Should_Create_A_Fresh_Sequence_Lifetime_After_Disconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            ITransportConnector connector = new TcpTransportConnector(
                endpoint,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeProvider.System);

            TransportFrame first = await ConnectAndReadFirstFrameAsync(connector, listener, timeout.Token);
            TransportFrame second = await ConnectAndReadFirstFrameAsync(connector, listener, timeout.Token);

            Assert.Equal((0U, 0U), (first.Sequence, second.Sequence));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TransportFrame> ConnectAndReadFirstFrameAsync(
        ITransportConnector connector,
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        ValueTask<Socket> accept = listener.AcceptSocketAsync(cancellationToken);
        await using ITransport transport = await connector.ConnectAsync(cancellationToken);
        await using var peer = TcpDuplexPipe.FromConnectedSocket(await accept);
        var message = new TransportMessage(7, Guid.NewGuid(), FrameFlags.None, new byte[] { 1 });

        await transport.SendAsync(message, cancellationToken);

        return await ReadFrameAsync(peer.Input, cancellationToken);
    }

    private static async Task<TransportFrame> ReadFrameAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        ReadResult result = await reader.ReadAsync(cancellationToken);
        ReadOnlySequence<byte> buffer = result.Buffer;
        bool decoded = FrameCodec.TryRead(ref buffer, out TransportFrame frame);
        reader.AdvanceTo(buffer.Start, buffer.End);
        return decoded ? frame : throw new InvalidDataException("No complete frame was received.");
    }
}
