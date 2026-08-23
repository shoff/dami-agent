using System.Buffers;
using Dami.Contracts.Transport;
using Dami.Transport.Framing;

namespace Dami.Transport.Tests.Framing;

public sealed class FrameCodecTests
{
    [Fact]
    public void Write_And_TryRead_Should_Round_Trip_A_Frame()
    {
        TransportFrame expected = CreateFrame();
        byte[] encoded = Encode(expected);
        var input = new ReadOnlySequence<byte>(encoded);

        bool result = FrameCodec.TryRead(ref input, out TransportFrame actual);

        Assert.Equal((true, Describe(expected), 0L), (result, Describe(actual), input.Length));
    }

    [Fact]
    public void TryRead_Should_Parse_A_Frame_Split_At_Every_Byte_Offset()
    {
        TransportFrame expected = CreateFrame();
        byte[] encoded = Encode(expected);
        bool allMatched = true;

        for (int split = 1; split < encoded.Length; split++)
        {
            ReadOnlySequence<byte> input = CreateSplitSequence(encoded, split);
            bool result = FrameCodec.TryRead(ref input, out TransportFrame actual);
            allMatched &= result && Describe(actual) == Describe(expected) && input.IsEmpty;
        }

        Assert.True(allMatched);
    }

    [Fact]
    public void TryRead_Should_Not_Consume_An_Incomplete_Frame()
    {
        byte[] encoded = Encode(CreateFrame());
        var input = new ReadOnlySequence<byte>(encoded.AsMemory(0, encoded.Length - 1));
        long initialLength = input.Length;

        bool result = FrameCodec.TryRead(ref input, out _);

        Assert.Equal((false, initialLength), (result, input.Length));
    }

    [Fact]
    public void TryRead_Should_Reject_A_Body_Shorter_Than_The_Header()
    {
        byte[] encoded = [1, 0];
        var input = new ReadOnlySequence<byte>(encoded);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => TryRead(input));

        Assert.Equal("The frame body is shorter than its fixed header.", exception.Message);
    }

    private static TransportFrame CreateFrame()
    {
        return new TransportFrame(
            FrameCodec.INITIAL_PROTOCOL_VERSION,
            42,
            9001,
            Guid.Parse("EA0BE158-6F34-43C0-93D2-D36CC63AC7E5"),
            FrameFlags.EndOfStream,
            new byte[] { 3, 1, 4, 1, 5, 9 });
    }

    private static byte[] Encode(TransportFrame frame)
    {
        var writer = new ArrayBufferWriter<byte>();
        FrameCodec.Write(writer, frame);
        return writer.WrittenSpan.ToArray();
    }

    private static string Describe(TransportFrame frame)
    {
        return $"{frame.ProtocolVersion}:{frame.MessageType}:{frame.Sequence}:" +
            $"{frame.CorrelationId:D}:{(byte)frame.Flags}:{Convert.ToHexString(frame.Payload.Span)}";
    }

    private static ReadOnlySequence<byte> CreateSplitSequence(
        byte[] encoded,
        int split)
    {
        var first = new BufferSegment(encoded.AsMemory(0, split));
        BufferSegment last = first.Append(encoded.AsMemory(split));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static void TryRead(ReadOnlySequence<byte> input)
    {
        FrameCodec.TryRead(ref input, out _);
    }
}
