using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

public sealed class LoopbackTransportTests
{
    [Fact]
    public async Task SendAsync_Should_Make_The_Frame_Available_To_ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var transport = new LoopbackTransport();
        var message = new TransportMessage(7, Guid.NewGuid(), FrameFlags.None, new byte[] { 2, 4, 6 });
        var expected = new TransportFrame(1, 7, 0, message.CorrelationId, FrameFlags.None, message.Payload);
        await transport.SendAsync(message, timeout.Token);

        TransportFrame actual = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constructor_Should_Reject_A_Nonpositive_Capacity()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoopbackTransport(0));

        Assert.Equal("capacity", exception.ParamName);
    }

    [Fact]
    public async Task SendAsync_Should_Snapshot_The_Payload_Before_Completing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var transport = new LoopbackTransport();
        byte[] payload = [2, 4, 6];
        var message = new TransportMessage(7, Guid.NewGuid(), FrameFlags.None, payload);
        await transport.SendAsync(message, timeout.Token);

        payload[0] = 99;
        TransportFrame received = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal((byte)2, received.Payload.Span[0]);
    }

    [Fact]
    public async Task SendAsync_Should_Assign_Protocol_Version_And_Sequence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var transport = new LoopbackTransport();
        var message = new TransportMessage(
            7,
            Guid.Parse("AABF2531-D0E9-49FE-BA93-ADBD4C2C5510"),
            FrameFlags.EndOfStream,
            new byte[] { 2, 4, 6 });

        await transport.SendAsync(message, timeout.Token);
        TransportFrame received = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal(
            (1, 0U, message.MessageType, message.CorrelationId, message.Flags),
            (received.ProtocolVersion, received.Sequence, received.MessageType, received.CorrelationId, received.Flags));
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
