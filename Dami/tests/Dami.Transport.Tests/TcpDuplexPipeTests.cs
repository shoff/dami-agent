using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace Dami.Transport.Tests;

public sealed class TcpDuplexPipeTests
{
    [Fact]
    public async Task FromConnectedSocket_Should_Adapt_An_Accepted_Socket_And_Enable_NoDelay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            ValueTask<Socket> accept = listener.AcceptSocketAsync(timeout.Token);
            using var client = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(endpoint, timeout.Token);
            Socket accepted = await accept;

            await using var connection = TcpDuplexPipe.FromConnectedSocket(accepted);

            Assert.True(accepted.NoDelay);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectAsync_Should_Read_Tcp_Peer_Bytes_From_The_Input_Pipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            ValueTask<TcpClient> accept = listener.AcceptTcpClientAsync(timeout.Token);
            await using TcpDuplexPipe connection = await TcpDuplexPipe.ConnectAsync(
                endpoint,
                timeout.Token);
            using TcpClient peer = await accept;

            await peer.GetStream().WriteAsync(new byte[] { 89 }, timeout.Token);
            byte actual = await ReadOneByteAsync(connection.Input, timeout.Token);

            Assert.Equal((byte)89, actual);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectAsync_Should_Write_Pipe_Bytes_To_The_Tcp_Peer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            ValueTask<TcpClient> accept = listener.AcceptTcpClientAsync(timeout.Token);
            await using TcpDuplexPipe connection = await TcpDuplexPipe.ConnectAsync(
                endpoint,
                timeout.Token);
            using TcpClient peer = await accept;

            await connection.Output.WriteAsync(new byte[] { 73 }, timeout.Token);
            byte[] received = new byte[1];
            int count = await peer.GetStream().ReadAsync(received, timeout.Token);

            Assert.Equal((1, (byte)73), (count, received[0]));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<byte> ReadOneByteAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        ReadResult result = await reader.ReadAsync(cancellationToken);
        ReadOnlySequence<byte> buffer = result.Buffer;
        byte value = buffer.FirstSpan[0];
        SequencePosition consumed = buffer.GetPosition(1);
        reader.AdvanceTo(consumed, consumed);
        return value;
    }
}
