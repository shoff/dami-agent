namespace Dami.Transport.Framing;

internal sealed class FrameSequenceTracker
{
    private uint expected;
    private bool initialized;

    public void Observe(uint sequence)
    {
        if (!this.initialized)
        {
            this.expected = unchecked(sequence + 1);
            this.initialized = true;
            return;
        }

        if (sequence != this.expected)
        {
            throw new InvalidDataException(
                $"Frame sequence mismatch: expected {this.expected}, received {sequence}.");
        }

        this.expected = unchecked(this.expected + 1);
    }
}
