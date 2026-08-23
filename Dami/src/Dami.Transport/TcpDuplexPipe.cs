using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace Dami.Transport;

/// <summary>Adapts one connected TCP socket to the pipelines duplex contract.</summary>
public sealed class TcpDuplexPipe : IDuplexPipe, IAsyncDisposable
{
    private readonly NetworkStream stream;

    private TcpDuplexPipe(NetworkStream stream)
    {
        this.stream = stream;
        this.Input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        this.Output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
    }

    /// <inheritdoc />
    public PipeReader Input { get; }

    /// <inheritdoc />
    public PipeWriter Output { get; }

    /// <summary>Takes ownership of a connected socket and exposes it as a pipelines connection.</summary>
    public static TcpDuplexPipe FromConnectedSocket(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        try
        {
            socket.NoDelay = true;
            return new TcpDuplexPipe(new NetworkStream(socket, ownsSocket: true));
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Connects one TCP socket and exposes it as a pipelines connection.</summary>
    public static async ValueTask<TcpDuplexPipe> ConnectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return FromConnectedSocket(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Completes both pipes and closes the underlying TCP connection.</summary>
    public async ValueTask DisposeAsync()
    {
        await this.Input.CompleteAsync().ConfigureAwait(false);
        await this.Output.CompleteAsync().ConfigureAwait(false);
        await this.stream.DisposeAsync().ConfigureAwait(false);
    }
}
