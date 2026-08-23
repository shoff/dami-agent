namespace Dami.Contracts.Transport;

/// <summary>Represents one versioned, serialization-agnostic transport frame.</summary>
public readonly record struct TransportFrame(
    ushort ProtocolVersion,
    ushort MessageType,
    uint Sequence,
    Guid CorrelationId,
    FrameFlags Flags,
    ReadOnlyMemory<byte> Payload)
{
    /// <summary>Determines whether every header field and payload byte is equal.</summary>
    public bool Equals(TransportFrame other)
    {
        return this.ProtocolVersion == other.ProtocolVersion &&
            this.MessageType == other.MessageType &&
            this.Sequence == other.Sequence &&
            this.CorrelationId == other.CorrelationId &&
            this.Flags == other.Flags &&
            this.Payload.Span.SequenceEqual(other.Payload.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.ProtocolVersion);
        hash.Add(this.MessageType);
        hash.Add(this.Sequence);
        hash.Add(this.CorrelationId);
        hash.Add(this.Flags);
        hash.AddBytes(this.Payload.Span);
        return hash.ToHashCode();
    }
}
