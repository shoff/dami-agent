using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dami.Contracts.Transport;

namespace Dami.Transport;

/// <summary>Provides deterministic in-process frame delivery for tests and local composition.</summary>
public sealed class LoopbackTransport : ITransport, IAsyncDisposable
{
    private readonly Channel<TransportFrame> frames;

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
    public ValueTask SendAsync(
        TransportFrame frame,
        CancellationToken cancellationToken)
    {
        TransportFrame snapshot = frame with { Payload = frame.Payload.ToArray() };
        return this.frames.Writer.WriteAsync(snapshot, cancellationToken);
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
