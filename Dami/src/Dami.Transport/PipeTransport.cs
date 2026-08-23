using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Dami.Contracts.Transport;
using Dami.Transport.Framing;

namespace Dami.Transport;

/// <summary>Delivers framed messages over an existing pipelines connection.</summary>
public sealed class PipeTransport : ITransport, IAsyncDisposable
{
    private readonly IDuplexPipe connection;
    private readonly FrameSequenceTracker inboundSequence = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private int disposed;
    private int receiveActive;

    /// <summary>Initializes a framed transport and takes ownership of the supplied connection.</summary>
    public PipeTransport(IDuplexPipe connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.connection = connection;
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(
        TransportFrame frame,
        CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        await this.sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this.ThrowIfDisposed();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                this.lifetimeCancellation.Token);
            await this.WriteFrameAsync(frame, linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            Volatile.Read(ref this.disposed) != 0 &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new EndOfStreamException("The transport output completed before the frame was accepted.");
        }
        finally
        {
            this.sendGate.Release();
        }
    }

    private async ValueTask WriteFrameAsync(
        TransportFrame frame,
        CancellationToken cancellationToken)
    {
        FrameCodec.Write(this.connection.Output, frame);
        FlushResult result = await this.connection.Output.FlushAsync(cancellationToken).ConfigureAwait(false);
        ValidateFlushResult(result);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TransportFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref this.receiveActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one receiver may enumerate a pipe transport at a time.");
        }

        try
        {
            await foreach (TransportFrame frame in this.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            Volatile.Write(ref this.receiveActive, 0);
        }
    }

    private async IAsyncEnumerable<TransportFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadResult result = await this.connection.Input.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            try
            {
                if (result.IsCanceled)
                {
                    throw new OperationCanceledException("The transport input read was canceled.");
                }

                while (FrameCodec.TryRead(ref buffer, out TransportFrame frame))
                {
                    this.inboundSequence.Observe(frame.Sequence);
                    yield return frame;
                }

                if (result.IsCompleted)
                {
                    EnsureNoTrailingData(buffer);
                    yield break;
                }
            }
            finally
            {
                this.connection.Input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }

    /// <summary>Completes both sides of the pipelines connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        await this.lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        await this.sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await this.connection.Input.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                await this.connection.Output.CompleteAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this.sendGate.Release();
            this.lifetimeCancellation.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PipeTransport));
        }
    }

    private static void ValidateFlushResult(FlushResult result)
    {
        if (result.IsCanceled)
        {
            throw new OperationCanceledException("The transport output flush was canceled.");
        }

        if (result.IsCompleted)
        {
            throw new EndOfStreamException("The transport output completed before the frame was accepted.");
        }
    }

    private static void EnsureNoTrailingData(ReadOnlySequence<byte> buffer)
    {
        if (!buffer.IsEmpty)
        {
            throw new InvalidDataException("The connection completed with an incomplete frame.");
        }
    }
}
