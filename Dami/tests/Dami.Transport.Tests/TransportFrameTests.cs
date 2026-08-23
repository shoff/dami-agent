using Dami.Contracts.Transport;

namespace Dami.Transport.Tests;

public sealed class TransportFrameTests
{
    [Fact]
    public void Equality_Should_Compare_Payload_Bytes()
    {
        var left = new TransportFrame(
            1,
            7,
            11,
            Guid.Parse("C241CB56-5FB4-417C-BEDE-391F823F68B6"),
            FrameFlags.EndOfStream,
            new byte[] { 2, 3, 5 });
        var right = left with { Payload = new byte[] { 2, 3, 5 } };

        Assert.Equal(left, right);
    }
}
