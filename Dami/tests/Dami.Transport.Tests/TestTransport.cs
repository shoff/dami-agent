using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

internal sealed class TestTransport : ITransport
{
    private readonly Channel<TransportFrame> received = Channel.CreateUnbounded<TransportFrame>();
    private readonly Channel<bool> receiveWaits = Channel.CreateUnbounded<bool>();
    private readonly Exception? receiveFailure;
    private readonly Exception? sendFailure;
    private readonly Channel<TransportMessage> sent = Channel.CreateUnbounded<TransportMessage>();

    public TestTransport(
        Exception? sendFailure = null,
        Exception? receiveFailure = null)
    {
        this.sendFailure = sendFailure;
        this.receiveFailure = receiveFailure;
    }

    public int DisposeCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        this.DisposeCount++;
        this.received.Writer.TryComplete();
        this.sent.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        if (this.sendFailure is not null)
        {
            return ValueTask.FromException(this.sendFailure);
        }

        return this.sent.Writer.WriteAsync(message, cancellationToken);
    }

    public async IAsyncEnumerable<TransportFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (this.receiveFailure is not null)
        {
            throw this.receiveFailure;
        }

        while (true)
        {
            await this.receiveWaits.Writer.WriteAsync(true, cancellationToken).ConfigureAwait(false);
            if (!await this.received.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            while (this.received.Reader.TryRead(out TransportFrame frame))
            {
                yield return frame;
            }
        }
    }

    public ValueTask<TransportMessage> ReadSentAsync(CancellationToken cancellationToken)
    {
        return this.sent.Reader.ReadAsync(cancellationToken);
    }

    public async ValueTask WaitForReceiveReadAsync(CancellationToken cancellationToken)
    {
        await this.receiveWaits.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void CompleteReceived()
    {
        this.received.Writer.TryComplete();
    }

    public ValueTask WriteReceivedAsync(
        TransportFrame frame,
        CancellationToken cancellationToken)
    {
        return this.received.Writer.WriteAsync(frame, cancellationToken);
    }
}
