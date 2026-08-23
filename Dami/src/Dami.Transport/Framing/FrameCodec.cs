using System.Buffers;
using System.Buffers.Binary;
using Dami.Contracts.Transport;

namespace Dami.Transport.Framing;

/// <summary>Reads and writes the binary envelope used by Dami transports.</summary>
public static class FrameCodec
{
    /// <summary>The first protocol version emitted by this codec.</summary>
    public const ushort INITIAL_PROTOCOL_VERSION = 1;

    /// <summary>The largest payload accepted by the framing layer.</summary>
    public const int MAX_PAYLOAD_LENGTH = 16 * 1024 * 1024;

    private const int FIXED_BODY_LENGTH = 25;
    private const int MAX_VARINT_LENGTH = 5;

    /// <summary>Writes one length-prefixed frame to the destination.</summary>
    public static void Write(
        IBufferWriter<byte> destination,
        TransportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (frame.Payload.Length > MAX_PAYLOAD_LENGTH)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Frame payload exceeds the maximum length.");
        }

        int bodyLength = checked(FIXED_BODY_LENGTH + frame.Payload.Length);
        WriteVarint(destination, (uint)bodyLength);

        Span<byte> header = destination.GetSpan(FIXED_BODY_LENGTH);
        BinaryPrimitives.WriteUInt16BigEndian(header, frame.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], frame.MessageType);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], frame.Sequence);
        frame.CorrelationId.TryWriteBytes(header[8..24], bigEndian: true, out _);
        header[24] = (byte)frame.Flags;
        destination.Advance(FIXED_BODY_LENGTH);

        WritePayload(destination, frame.Payload.Span);
    }

    /// <summary>Attempts to read one complete frame without consuming incomplete input.</summary>
    public static bool TryRead(
        ref ReadOnlySequence<byte> input,
        out TransportFrame frame)
    {
        var reader = new SequenceReader<byte>(input);
        if (!TryReadVarint(ref reader, out uint bodyLength))
        {
            frame = default;
            return false;
        }

        ValidateBodyLength(bodyLength);
        if (reader.Remaining < bodyLength)
        {
            frame = default;
            return false;
        }

        frame = ReadBody(ref reader, checked((int)bodyLength));
        input = input.Slice(reader.Position);
        return true;
    }

    private static TransportFrame ReadBody(
        ref SequenceReader<byte> reader,
        int bodyLength)
    {
        Span<byte> header = stackalloc byte[FIXED_BODY_LENGTH];
        if (!reader.TryCopyTo(header))
        {
            throw new InvalidDataException("The frame header is incomplete.");
        }

        reader.Advance(FIXED_BODY_LENGTH);
        int payloadLength = bodyLength - FIXED_BODY_LENGTH;
        byte[] payload = new byte[payloadLength];
        if (!reader.TryCopyTo(payload))
        {
            throw new InvalidDataException("The frame payload is incomplete.");
        }

        reader.Advance(payloadLength);
        return new TransportFrame(
            BinaryPrimitives.ReadUInt16BigEndian(header),
            BinaryPrimitives.ReadUInt16BigEndian(header[2..]),
            BinaryPrimitives.ReadUInt32BigEndian(header[4..]),
            new Guid(header[8..24], bigEndian: true),
            (FrameFlags)header[24],
            payload);
    }

    private static bool TryReadVarint(
        ref SequenceReader<byte> reader,
        out uint value)
    {
        value = 0;
        for (int index = 0; index < MAX_VARINT_LENGTH; index++)
        {
            if (!reader.TryRead(out byte current))
            {
                return false;
            }

            value |= (uint)(current & 0x7F) << (index * 7);
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        throw new InvalidDataException("The frame length prefix is invalid.");
    }

    private static void ValidateBodyLength(uint bodyLength)
    {
        if (bodyLength < FIXED_BODY_LENGTH)
        {
            throw new InvalidDataException("The frame body is shorter than its fixed header.");
        }

        if (bodyLength > FIXED_BODY_LENGTH + MAX_PAYLOAD_LENGTH)
        {
            throw new InvalidDataException("The frame body exceeds the maximum length.");
        }
    }

    private static void WriteVarint(
        IBufferWriter<byte> destination,
        uint value)
    {
        Span<byte> encoded = stackalloc byte[MAX_VARINT_LENGTH];
        int length = 0;
        do
        {
            byte current = (byte)(value & 0x7F);
            value >>= 7;
            encoded[length] = value == 0 ? current : (byte)(current | 0x80);
            length++;
        }
        while (value != 0);

        Span<byte> output = destination.GetSpan(length);
        encoded[..length].CopyTo(output);
        destination.Advance(length);
    }

    private static void WritePayload(
        IBufferWriter<byte> destination,
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        Span<byte> output = destination.GetSpan(payload.Length);
        payload.CopyTo(output);
        destination.Advance(payload.Length);
    }
}
