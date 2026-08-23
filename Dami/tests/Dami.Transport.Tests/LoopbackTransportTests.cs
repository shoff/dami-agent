using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

public sealed class LoopbackTransportTests
{
    [Fact]
    public async Task SendAsync_Should_Make_The_Frame_Available_To_ReceiveAsync()
    {
        await using var transport = new LoopbackTransport();
        var expected = new TransportFrame(1, 7, 11, Guid.NewGuid(), FrameFlags.None, new byte[] { 2, 4, 6 });
        await transport.SendAsync(expected, CancellationToken.None);

        TransportFrame actual = await ReceiveOneAsync(transport, CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constructor_Should_Reject_A_Nonpositive_Capacity()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoopbackTransport(0));

        Assert.Equal("capacity", exception.ParamName);
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
