using System.Runtime.CompilerServices;
using Dami.Contracts.Transport;

namespace Dami.Transport;

/// <summary>Adds connection heartbeat policy to another transport.</summary>
public sealed class HeartbeatTransport : ITransport
{
    private const ushort HEARTBEAT_MESSAGE_TYPE = 0;

    private readonly object disposalSync = new();
    private readonly TimeSpan interval;
    private readonly ITransport inner;
    private readonly TimeSpan silenceTimeout;
    private readonly TimeProvider timeProvider;
    private Task? disposal;
    private int receiveActive;

    /// <summary>Initializes heartbeat policy and takes ownership of the wrapped transport.</summary>
    public HeartbeatTransport(
        ITransport inner,
        TimeSpan interval,
        TimeSpan silenceTimeout,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateTiming(interval, silenceTimeout);
        this.inner = inner;
        this.interval = interval;
        this.silenceTimeout = silenceTimeout;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public ValueTask SendAsync(
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        if (message.MessageType == HEARTBEAT_MESSAGE_TYPE)
        {
            throw new ArgumentException("Message type 0 is reserved for transport heartbeat.", nameof(message));
        }

        return this.inner.SendAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (this.disposalSync)
        {
            this.disposal ??= this.inner.DisposeAsync().AsTask();
            return new ValueTask(this.disposal);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TransportFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref this.receiveActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Only one receiver may enumerate a heartbeat transport at a time.");
        }

        try
        {
            await foreach (TransportFrame frame in this
                .ReceiveCoreAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            Volatile.Write(ref this.receiveActive, 0);
        }
    }

    private async IAsyncEnumerable<TransportFrame> ReceiveCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var receiveLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeatTask = this.SendHeartbeatsAsync(receiveLifetime);
        try
        {
            await foreach (TransportFrame frame in this
                .ReadFramesAsync(receiveLifetime)
                .ConfigureAwait(false))
            {
                if (frame.MessageType == HEARTBEAT_MESSAGE_TYPE)
                {
                    ValidateHeartbeat(frame);
                    continue;
                }

                yield return frame;
            }
        }
        finally
        {
            await receiveLifetime.CancelAsync().ConfigureAwait(false);
            await heartbeatTask.ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<TransportFrame> ReadFramesAsync(
        CancellationTokenSource receiveLifetime)
    {
        IAsyncEnumerator<TransportFrame> frames = this.inner
            .ReceiveAsync(receiveLifetime.Token)
            .GetAsyncEnumerator();
        var disposeEnumerator = true;
        try
        {
            while (true)
            {
                var (hasNext, timeout) = await this
                    .MoveNextAsync(frames, receiveLifetime).ConfigureAwait(false);
                if (timeout is not null)
                {
                    disposeEnumerator = false;
                    throw timeout;
                }
                if (!hasNext)
                {
                    yield break;
                }
                yield return frames.Current;
            }
        }
        finally
        {
            if (disposeEnumerator)
            {
                await frames.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<(bool HasNext, TimeoutException? Timeout)> MoveNextAsync(
        IAsyncEnumerator<TransportFrame> frames,
        CancellationTokenSource receiveLifetime)
    {
        Task<bool> moveNext = frames.MoveNextAsync().AsTask();
        try
        {
            var hasNext = await moveNext.WaitAsync(
                this.silenceTimeout, this.timeProvider).ConfigureAwait(false);
            return (hasNext, null);
        }
        catch (TimeoutException exception) when (!moveNext.IsCompleted)
        {
            await receiveLifetime.CancelAsync().ConfigureAwait(false);
            return (false, new TimeoutException(
                "No frame was received within the configured silence timeout.",
                exception));
        }
    }

    private async Task SendHeartbeatsAsync(CancellationTokenSource receiveLifetime)
    {
        CancellationToken cancellationToken = receiveLifetime.Token;
        try
        {
            while (true)
            {
                await Task.Delay(
                    this.interval,
                    this.timeProvider,
                    cancellationToken).ConfigureAwait(false);
                await this.inner.SendAsync(
                    CreateHeartbeat(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            await receiveLifetime.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static TransportMessage CreateHeartbeat()
    {
        return new TransportMessage(
            HEARTBEAT_MESSAGE_TYPE,
            Guid.Empty,
            FrameFlags.None,
            ReadOnlyMemory<byte>.Empty);
    }

    private static void ValidateHeartbeat(TransportFrame frame)
    {
        if (frame.CorrelationId != Guid.Empty ||
            frame.Flags != FrameFlags.None ||
            !frame.Payload.IsEmpty)
        {
            throw new InvalidDataException("Heartbeat frame contains application data.");
        }
    }

    internal static void ValidateTiming(
        TimeSpan interval,
        TimeSpan silenceTimeout)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (silenceTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(silenceTimeout));
        }

        if (interval >= silenceTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "Heartbeat interval must be shorter than the silence timeout.");
        }
    }
}
