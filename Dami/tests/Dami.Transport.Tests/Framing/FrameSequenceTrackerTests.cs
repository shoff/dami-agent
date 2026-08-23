using Dami.Transport.Framing;

namespace Dami.Transport.Tests.Framing;

public sealed class FrameSequenceTrackerTests
{
    [Theory]
    [InlineData(29U)]
    [InlineData(28U)]
    [InlineData(31U)]
    public void Observe_Should_Reject_Any_Noncontiguous_Sequence(uint received)
    {
        var tracker = new FrameSequenceTracker();
        tracker.Observe(29);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => tracker.Observe(received));

        Assert.Equal($"Frame sequence mismatch: expected 30, received {received}.", exception.Message);
    }

    [Fact]
    public void Observe_Should_Accept_UInt32_Wraparound()
    {
        var tracker = new FrameSequenceTracker();

        tracker.Observe(uint.MaxValue);
        Exception? exception = Record.Exception(() => tracker.Observe(0));

        Assert.Null(exception);
    }
}
