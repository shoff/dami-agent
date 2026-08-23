using System.Buffers;
using System.IO.Pipelines;
using Dami.Contracts.Transport;
using Dami.Transport.Framing;

namespace Dami.Transport.Tests;

public sealed class PipeTransportTests
{
    [Fact]
    public async Task SendAsync_Should_Write_A_Complete_Frame_To_The_Output_Pipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        TransportFrame expected = CreateFrame();

        await transport.SendAsync(expected, timeout.Token);
        TransportFrame actual = await ReadFrameAsync(outbound.Reader, timeout.Token);

        Assert.Equal(Describe(expected), Describe(actual));
    }

    [Fact]
    public async Task ReceiveAsync_Should_Yield_A_Complete_Frame_From_The_Input_Pipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        TransportFrame expected = CreateFrame();
        await WriteFrameAsync(inbound.Writer, expected, timeout.Token);

        TransportFrame actual = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal(Describe(expected), Describe(actual));
    }

    [Fact]
    public async Task SendAsync_Should_Serialize_Overlapping_Writes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        TransportFrame first = CreateFrame();
        TransportFrame second = first with { Sequence = first.Sequence + 1 };

        ValueTask firstSend = transport.SendAsync(first, timeout.Token);
        Assert.False(firstSend.IsCompleted);
        ValueTask secondSend = transport.SendAsync(second, timeout.Token);
        Task<TransportFrame[]> receive = ReadFramesAsync(outbound.Reader, 2, timeout.Token);

        await firstSend;
        await secondSend;
        TransportFrame[] actual = await receive;
        Assert.Equal([first, second], actual);
    }

    [Fact]
    public async Task SendAsync_Should_Fail_When_The_Output_Reader_Has_Completed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        await outbound.Reader.CompleteAsync();

        EndOfStreamException exception = await Assert.ThrowsAsync<EndOfStreamException>(
            () => transport.SendAsync(CreateFrame(), timeout.Token).AsTask());

        Assert.Equal("The transport output completed before the frame was accepted.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Should_Fail_When_The_Output_Flush_Is_Canceled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        outbound.Writer.CancelPendingFlush();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.SendAsync(CreateFrame(), timeout.Token).AsTask());

        Assert.Equal("The transport output flush was canceled.", exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Fail_When_The_Input_Read_Is_Canceled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        await using IAsyncEnumerator<TransportFrame> enumerator = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        inbound.Reader.CancelPendingRead();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("The transport input read was canceled.", exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Reject_A_Second_Active_Receiver()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        await using IAsyncEnumerator<TransportFrame> first = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();
        Task<bool> firstMove = first.MoveNextAsync().AsTask();
        await using IAsyncEnumerator<TransportFrame> second = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.MoveNextAsync().AsTask());

        Assert.Equal("Only one receiver may enumerate a pipe transport at a time.", exception.Message);
        await inbound.Writer.CompleteAsync();
        Assert.False(await firstMove);
    }

    [Fact]
    public async Task DisposeAsync_Should_Complete_The_Output_When_Input_Completion_Fails()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(new ThrowingCompletePipeReader(), outbound.Writer);
        var transport = new PipeTransport(connection);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.DisposeAsync().AsTask());
        ReadResult output = await outbound.Reader.ReadAsync(timeout.Token);

        Assert.Equal("Input completion failed.", exception.Message);
        Assert.True(output.IsCompleted);
        outbound.Reader.AdvanceTo(output.Buffer.End);
        await outbound.Reader.CompleteAsync();
    }

    [Fact]
    public async Task SendAsync_Should_Reject_A_Send_After_Disposal()
    {
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        var transport = new PipeTransport(connection);
        await transport.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => transport.SendAsync(CreateFrame(), CancellationToken.None).AsTask());

        Assert.Equal(nameof(PipeTransport), exception.ObjectName);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Reject_A_Receiver_After_Disposal()
    {
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        var transport = new PipeTransport(connection);
        await transport.DisposeAsync();
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => receiver.MoveNextAsync().AsTask());

        Assert.Equal(nameof(PipeTransport), exception.ObjectName);
    }

    [Fact]
    public async Task DisposeAsync_Should_Unblock_A_Backpressured_Send()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        var transport = new PipeTransport(connection);
        ValueTask send = transport.SendAsync(CreateFrame(), CancellationToken.None);
        Assert.False(send.IsCompleted);

        await transport.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        EndOfStreamException exception = await Assert.ThrowsAsync<EndOfStreamException>(
            () => send.AsTask());

        Assert.Equal("The transport output completed before the frame was accepted.", exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Should_Reject_A_Sequence_Gap()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inbound = new Pipe();
        var outbound = new Pipe();
        var connection = new TestDuplexPipe(inbound.Reader, outbound.Writer);
        await using var transport = new PipeTransport(connection);
        TransportFrame first = CreateFrame();
        TransportFrame afterGap = first with { Sequence = first.Sequence + 2 };
        await WriteFrameAsync(inbound.Writer, first, timeout.Token);
        await WriteFrameAsync(inbound.Writer, afterGap, timeout.Token);
        await using IAsyncEnumerator<TransportFrame> receiver = transport
            .ReceiveAsync(timeout.Token)
            .GetAsyncEnumerator();

        Assert.True(await receiver.MoveNextAsync());
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => receiver.MoveNextAsync().AsTask());

        Assert.Equal("Frame sequence mismatch: expected 30, received 31.", exception.Message);
    }

    private static TransportFrame CreateFrame()
    {
        return new TransportFrame(
            FrameCodec.INITIAL_PROTOCOL_VERSION,
            17,
            29,
            Guid.Parse("06756B26-357A-478C-A3CE-8AE55015DBA9"),
            FrameFlags.None,
            new byte[] { 8, 13, 21 });
    }

    private static async Task WriteFrameAsync(
        PipeWriter writer,
        TransportFrame frame,
        CancellationToken cancellationToken)
    {
        var encoded = new ArrayBufferWriter<byte>();
        FrameCodec.Write(encoded, frame);
        await writer.WriteAsync(encoded.WrittenMemory, cancellationToken);
    }

    private static async Task<TransportFrame> ReceiveOneAsync(
        PipeTransport transport,
        CancellationToken cancellationToken)
    {
        await foreach (TransportFrame frame in transport.ReceiveAsync(cancellationToken))
        {
            return frame;
        }

        throw new InvalidOperationException("The transport completed without yielding a frame.");
    }

    private static async Task<TransportFrame> ReadFrameAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        ReadResult result = await reader.ReadAsync(cancellationToken);
        ReadOnlySequence<byte> buffer = result.Buffer;
        bool decoded = FrameCodec.TryRead(ref buffer, out TransportFrame frame);
        reader.AdvanceTo(buffer.Start, buffer.End);
        return decoded ? frame : throw new InvalidDataException("No complete frame was written.");
    }

    private static async Task<TransportFrame[]> ReadFramesAsync(
        PipeReader reader,
        int count,
        CancellationToken cancellationToken)
    {
        var frames = new List<TransportFrame>(count);
        while (frames.Count < count)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = result.Buffer;
            while (FrameCodec.TryRead(ref buffer, out TransportFrame frame))
            {
                frames.Add(frame);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }

        return frames.ToArray();
    }

    private static string Describe(TransportFrame frame)
    {
        return $"{frame.ProtocolVersion}:{frame.MessageType}:{frame.Sequence}:" +
            $"{frame.CorrelationId:D}:{(byte)frame.Flags}:{Convert.ToHexString(frame.Payload.Span)}";
    }

    private sealed class TestDuplexPipe : IDuplexPipe
    {
        public TestDuplexPipe(
            PipeReader input,
            PipeWriter output)
        {
            this.Input = input;
            this.Output = output;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }
    }

}
