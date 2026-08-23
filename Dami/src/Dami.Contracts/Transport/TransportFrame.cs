namespace Dami.Contracts.Transport;

/// <summary>Represents one versioned, serialization-agnostic transport frame.</summary>
public readonly record struct TransportFrame(
    ushort ProtocolVersion,
    ushort MessageType,
    uint Sequence,
    Guid CorrelationId,
    FrameFlags Flags,
    ReadOnlyMemory<byte> Payload);
