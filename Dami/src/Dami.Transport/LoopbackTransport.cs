using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dami.Contracts.Transport;
using Dami.Transport.Framing;

namespace Dami.Transport;

/// <summary>Provides deterministic in-process frame delivery for tests and local composition.</summary>
public sealed class LoopbackTransport : ITransport, IAsyncDisposable
{
    private readonly Channel<TransportFrame> frames;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private uint nextOutboundSequence;

    /// <summary>Initializes a loopback transport with bounded backpressure.</summary>
    public LoopbackTransport(int capacity = 256)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.frames = Channel.CreateBounded<TransportFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        await this.sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var frame = new TransportFrame(
                FrameCodec.INITIAL_PROTOCOL_VERSION,
                message.MessageType,
                this.nextOutboundSequence,
                message.CorrelationId,
                message.Flags,
                message.Payload.ToArray());
            await this.frames.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            this.nextOutboundSequence = unchecked(this.nextOutboundSequence + 1);
        }
        finally
        {
            this.sendGate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TransportFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (TransportFrame frame in this.frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <summary>Stops accepting frames and completes receivers after queued frames are read.</summary>
    public ValueTask DisposeAsync()
    {
        this.frames.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
