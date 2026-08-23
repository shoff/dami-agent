using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

public sealed class HeartbeatTransportTests
{
    [Fact]
    public async Task Inbound_Heartbeat_Should_Reset_The_Silence_Timeout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = new TestTimeProvider();
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, clock);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        Task<bool> receive = receiver.MoveNextAsync().AsTask();
        await inner.WaitForReceiveReadAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await inner.WriteReceivedAsync(CreateHeartbeatFrame(5), timeout.Token);
        await inner.WaitForReceiveReadAsync(timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(14));

        Assert.False(receive.IsCompleted);
        TransportFrame expected = CreateApplicationFrame(6);
        await inner.WriteReceivedAsync(expected, timeout.Token);
        Assert.True(await receive);
        Assert.Equal(expected, receiver.Current);
    }

    [Fact]
    public async Task Heartbeat_Should_Share_The_Inner_Sequence_Without_Reaching_The_Caller()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = new TestTimeProvider();
        await using var loopback = new LoopbackTransport();
        var observing = new ObservingTransport(loopback);
        HeartbeatTransport transport = CreateTransport(observing, clock);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        TransportMessage firstMessage = CreateApplicationMessage(7);
        await transport.SendAsync(firstMessage, timeout.Token);
        await observing.ReadSentAsync(timeout.Token);
        Assert.True(await receiver.MoveNextAsync());
        TransportFrame first = receiver.Current;

        Task<bool> secondMove = receiver.MoveNextAsync().AsTask();
        clock.Advance(TimeSpan.FromSeconds(5));
        TransportMessage heartbeat = await observing.ReadSentAsync(timeout.Token);
        await transport.SendAsync(CreateApplicationMessage(8), timeout.Token);
        Assert.True(await secondMove);

        Assert.Equal((0U, (ushort)7), (first.Sequence, first.MessageType));
        Assert.Equal((ushort)0, heartbeat.MessageType);
        Assert.Equal((2U, (ushort)8), (receiver.Current.Sequence, receiver.Current.MessageType));
    }

    [Fact]
    public async Task ReceiveAsync_Should_Reject_A_Second_Active_Receiver()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, new TestTimeProvider());
        await using IAsyncEnumerator<TransportFrame> first = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        Task<bool> firstMove = first.MoveNextAsync().AsTask();
        await using IAsyncEnumerator<TransportFrame> second = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.MoveNextAsync().AsTask());

        Assert.Equal("Only one receiver may enumerate a heartbeat transport at a time.", exception.Message);
        inner.CompleteReceived();
        Assert.False(await firstMove);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Preserve_An_Inner_Timeout_Failure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expected = new TimeoutException("Inner transport timeout.");
        var inner = new TestTransport(receiveFailure: expected);
        HeartbeatTransport transport = CreateTransport(inner, new TestTimeProvider());

        TimeoutException actual = await Assert.ThrowsAsync<TimeoutException>(
            () => ReceiveOneAsync(transport, timeout.Token));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Surface_A_Heartbeat_Send_Failure_Immediately()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var clock = new TestTimeProvider();
        var expected = new IOException("Heartbeat send failed.");
        var inner = new TestTransport(expected);
        HeartbeatTransport transport = CreateTransport(inner, clock);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        Task<bool> receive = receiver.MoveNextAsync().AsTask();

        clock.Advance(TimeSpan.FromSeconds(5));
        Exception? failure = null;
        try
        {
            await receive;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.Same(expected, failure);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0, 15, "interval")]
    [InlineData(5, 0, "silenceTimeout")]
    [InlineData(15, 15, "interval")]
    [InlineData(16, 15, "interval")]
    public void Constructor_Should_Reject_Invalid_Timing(
        int intervalSeconds,
        int timeoutSeconds,
        string parameterName)
    {
        var inner = new TestTransport();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new HeartbeatTransport(
                inner,
                TimeSpan.FromSeconds(intervalSeconds),
                TimeSpan.FromSeconds(timeoutSeconds),
                new TestTimeProvider()));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Fail_After_Inbound_Silence_Timeout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = new TestTimeProvider();
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, clock);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        Task<bool> receive = receiver.MoveNextAsync().AsTask();

        clock.Advance(TimeSpan.FromSeconds(15));
        Exception? failure = null;
        try
        {
            await receive;
        }
        catch (Exception observedFailure)
        {
            failure = observedFailure;
        }

        TimeoutException timeoutException = Assert.IsType<TimeoutException>(failure);
        Assert.Equal("No frame was received within the configured silence timeout.", timeoutException.Message);
    }

    [Fact]
    public async Task SendAsync_Should_Reject_The_Reserved_Heartbeat_Type()
    {
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, new TestTimeProvider());
        var message = new TransportMessage(0, Guid.Empty, FrameFlags.None, ReadOnlyMemory<byte>.Empty);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => transport.SendAsync(message, CancellationToken.None).AsTask());

        Assert.Equal("message", exception.ParamName);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ReceiveAsync_Should_Reject_A_Malformed_Heartbeat(
        bool hasCorrelationId,
        bool hasFlags,
        bool hasPayload)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, new TestTimeProvider());
        var heartbeat = new TransportFrame(
            1,
            0,
            5,
            hasCorrelationId ? Guid.NewGuid() : Guid.Empty,
            hasFlags ? FrameFlags.Error : FrameFlags.None,
            hasPayload ? new byte[] { 1 } : ReadOnlyMemory<byte>.Empty);
        await inner.WriteReceivedAsync(heartbeat, timeout.Token);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ReceiveOneAsync(transport, timeout.Token));

        Assert.Equal("Heartbeat frame contains application data.", exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Filter_A_Valid_Heartbeat()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = new TestTimeProvider();
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, clock);
        var heartbeat = new TransportFrame(1, 0, 5, Guid.Empty, FrameFlags.None, ReadOnlyMemory<byte>.Empty);
        var expected = new TransportFrame(1, 7, 6, Guid.NewGuid(), FrameFlags.None, new byte[] { 1 });
        await inner.WriteReceivedAsync(heartbeat, timeout.Token);
        await inner.WriteReceivedAsync(expected, timeout.Token);

        TransportFrame actual = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SendLoop_Should_Send_A_Heartbeat_After_The_Interval()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var receiveCancellation = new CancellationTokenSource();
        var clock = new TestTimeProvider();
        var inner = new TestTransport();
        HeartbeatTransport transport = CreateTransport(inner, clock);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(receiveCancellation.Token)
            .GetAsyncEnumerator();
        Task<bool> receive = receiver.MoveNextAsync().AsTask();

        clock.Advance(TimeSpan.FromSeconds(5));
        TransportMessage actual = await inner.ReadSentAsync(timeout.Token);

        Assert.Equal(((ushort)0, Guid.Empty, FrameFlags.None),
            (actual.MessageType, actual.CorrelationId, actual.Flags));
        Assert.True(actual.Payload.IsEmpty);
        await receiveCancellation.CancelAsync();
        OperationCanceledException? cancellation = null;
        try
        {
            await receive;
        }
        catch (OperationCanceledException exception)
        {
            cancellation = exception;
        }

        Assert.NotNull(cancellation);
    }

    private static HeartbeatTransport CreateTransport(
        ITransport inner,
        TimeProvider clock)
    {
        return new HeartbeatTransport(
            inner,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            clock);
    }

    private static TransportMessage CreateApplicationMessage(ushort messageType)
    {
        return new TransportMessage(
            messageType,
            Guid.NewGuid(),
            FrameFlags.None,
            new byte[] { 1 });
    }

    private static TransportFrame CreateApplicationFrame(uint sequence)
    {
        return new TransportFrame(
            1,
            7,
            sequence,
            Guid.NewGuid(),
            FrameFlags.None,
            new byte[] { 1 });
    }

    private static TransportFrame CreateHeartbeatFrame(uint sequence)
    {
        return new TransportFrame(
            1,
            0,
            sequence,
            Guid.Empty,
            FrameFlags.None,
            ReadOnlyMemory<byte>.Empty);
    }

    private static async Task<TransportFrame> ReceiveOneAsync(
        ITransport transport,
        CancellationToken cancellationToken)
    {
        await foreach (TransportFrame frame in transport.ReceiveAsync(cancellationToken))
        {
            return frame;
        }

        throw new InvalidOperationException("The transport completed without yielding a frame.");
    }
}
