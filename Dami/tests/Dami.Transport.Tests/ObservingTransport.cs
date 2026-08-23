using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

internal sealed class ObservingTransport : ITransport
{
    private readonly ITransport inner;
    private readonly Channel<TransportMessage> sent = Channel.CreateUnbounded<TransportMessage>();

    public ObservingTransport(ITransport inner)
    {
        this.inner = inner;
    }

    public async ValueTask SendAsync(
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        await this.inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await this.sent.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TransportFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (TransportFrame frame in this.inner
            .ReceiveAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    public ValueTask<TransportMessage> ReadSentAsync(CancellationToken cancellationToken)
    {
        return this.sent.Reader.ReadAsync(cancellationToken);
    }
}
