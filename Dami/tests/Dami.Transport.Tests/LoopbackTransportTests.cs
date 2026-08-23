using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

public sealed class LoopbackTransportTests
{
    [Fact]
    public async Task SendAsync_Should_Make_The_Frame_Available_To_ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var transport = new LoopbackTransport();
        var expected = new TransportFrame(1, 7, 11, Guid.NewGuid(), FrameFlags.None, new byte[] { 2, 4, 6 });
        await transport.SendAsync(expected, timeout.Token);

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
        var sent = new TransportFrame(1, 7, 11, Guid.NewGuid(), FrameFlags.None, payload);
        await transport.SendAsync(sent, timeout.Token);

        payload[0] = 99;
        TransportFrame received = await ReceiveOneAsync(transport, timeout.Token);

        Assert.Equal((byte)2, received.Payload.Span[0]);
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
