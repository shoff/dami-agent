using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace Dami.Transport;

/// <summary>Adapts one connected TCP socket to the pipelines duplex contract.</summary>
public sealed class TcpDuplexPipe : IDuplexPipe, IAsyncDisposable
{
    private readonly object disposalSync = new();
    private readonly IAsyncDisposable lifetime;
    private Task? disposal;

    private TcpDuplexPipe(NetworkStream stream)
    {
        this.lifetime = stream;
        this.Input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        this.Output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
    }

    internal TcpDuplexPipe(
        PipeReader input,
        PipeWriter output,
        IAsyncDisposable lifetime)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(lifetime);
        this.Input = input;
        this.Output = output;
        this.lifetime = lifetime;
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
    public ValueTask DisposeAsync()
    {
        lock (this.disposalSync)
        {
            this.disposal ??= this.DisposeCoreAsync();
            return new ValueTask(this.disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            try
            {
                await this.Input.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                await this.Output.CompleteAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await this.lifetime.DisposeAsync().ConfigureAwait(false);
        }
    }
}
