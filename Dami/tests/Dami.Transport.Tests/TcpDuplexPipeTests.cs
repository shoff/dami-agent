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

    [Fact]
    public async Task DisposeAsync_Should_Clean_All_Resources_When_Input_Completion_Fails()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var output = new Pipe();
        var lifetime = new TrackingAsyncDisposable();
        var connection = new TcpDuplexPipe(
            new ThrowingCompletePipeReader(),
            output.Writer,
            lifetime);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.DisposeAsync().AsTask());
        ReadResult outputResult = await output.Reader.ReadAsync(timeout.Token);

        Assert.Equal("Input completion failed.", exception.Message);
        Assert.True(outputResult.IsCompleted);
        Assert.True(lifetime.IsDisposed);
        output.Reader.AdvanceTo(outputResult.Buffer.End);
        await output.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_The_Owned_Lifetime_Only_Once()
    {
        var input = new Pipe();
        var output = new Pipe();
        var lifetime = new TrackingAsyncDisposable();
        var connection = new TcpDuplexPipe(input.Reader, output.Writer, lifetime);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.Equal(1, lifetime.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_Should_Share_One_Completion_Between_Overlapping_Callers()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var input = new Pipe();
        var output = new Pipe();
        var lifetime = new BlockingAsyncDisposable();
        var connection = new TcpDuplexPipe(input.Reader, output.Writer, lifetime);

        ValueTask first = connection.DisposeAsync();
        await lifetime.Entered.WaitAsync(timeout.Token);
        ValueTask second = connection.DisposeAsync();
        Assert.False(second.IsCompleted);
        lifetime.Release();
        await Task.WhenAll(first.AsTask(), second.AsTask());

        Assert.Equal(1, lifetime.DisposeCount);
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

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public bool IsDisposed => this.DisposeCount > 0;

        public ValueTask DisposeAsync()
        {
            this.DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingAsyncDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim release = new(0, 1);

        public int DisposeCount { get; private set; }

        public Task Entered => this.entered.Task;

        public async ValueTask DisposeAsync()
        {
            this.DisposeCount++;
            this.entered.TrySetResult();
            await this.release.WaitAsync().ConfigureAwait(false);
        }

        public void Release()
        {
            this.release.Release();
        }
    }
}
