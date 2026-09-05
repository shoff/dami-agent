using System.Text.Json.Serialization;

namespace Dami.Gui;

/// <summary>A user-addressed GUI turn, which always uses the subscription frontier.</summary>
public sealed record DirectChatRequest
{
    /// <summary>Creates a direct turn.</summary>
    public DirectChatRequest(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.Message = message;
    }

    /// <summary>The user's message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; }

    /// <summary>Direct GUI conversation always goes straight to the subscription.</summary>
    [JsonPropertyName("frontier")]
    public bool Frontier => true;

    /// <summary>Direct GUI conversation never invokes local augmentation.</summary>
    [JsonPropertyName("augmented")]
    public bool Augmented => false;

    /// <summary>An optional image, sent only to the loopback runtime.</summary>
    [JsonPropertyName("images")]
    public IReadOnlyList<DirectChatImage> Images { get; init; } = [];
}

/// <summary>One bounded image attached to a direct GUI turn.</summary>
public sealed record DirectChatImage
{
    /// <summary>Creates an attachment.</summary>
    public DirectChatImage(string fileName, string contentType, byte[] bytes)
    {
        this.FileName = fileName;
        this.ContentType = contentType;
        this.Bytes = bytes;
    }

    [JsonPropertyName("fileName")]
    public string FileName { get; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; }

    [JsonPropertyName("bytes")]
    public byte[] Bytes { get; }
}
