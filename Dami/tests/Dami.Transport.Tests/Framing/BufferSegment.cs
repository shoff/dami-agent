using System.Buffers;

namespace Dami.Transport.Tests.Framing;

public sealed class BufferSegment : ReadOnlySequenceSegment<byte>
{
    public BufferSegment(ReadOnlyMemory<byte> memory)
    {
        this.Memory = memory;
    }

    public BufferSegment Append(ReadOnlyMemory<byte> memory)
    {
        var segment = new BufferSegment(memory)
        {
            RunningIndex = this.RunningIndex + this.Memory.Length
        };
        this.Next = segment;
        return segment;
    }
}
