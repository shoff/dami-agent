namespace Dami.Contracts.Transport;

/// <summary>Represents application-owned content to be framed by a transport.</summary>
public readonly struct TransportMessage
{
    /// <summary>Initializes application-owned transport content.</summary>
    public TransportMessage(
        ushort messageType,
        Guid correlationId,
        FrameFlags flags,
        ReadOnlyMemory<byte> payload)
    {
        this.MessageType = messageType;
        this.CorrelationId = correlationId;
        this.Flags = flags;
        this.Payload = payload;
    }

    /// <summary>Gets the application-defined message type.</summary>
    public ushort MessageType { get; }

    /// <summary>Gets the identifier shared by related messages.</summary>
    public Guid CorrelationId { get; }

    /// <summary>Gets protocol-level handling requested for the message.</summary>
    public FrameFlags Flags { get; }

    /// <summary>Gets the serialization-agnostic application payload.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}
