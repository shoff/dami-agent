using System.Text.Json;

namespace Dami.Gateway.Discord;

/// <summary>Discord gateway opcodes, of which this gateway uses a deliberate few.</summary>
public enum DiscordOpcode
{
    /// <summary>An event. The only opcode that carries a name.</summary>
    Dispatch = 0,

    /// <summary>Keepalive, sent on the interval Discord names in HELLO.</summary>
    Heartbeat = 1,

    /// <summary>Authenticates the socket.</summary>
    Identify = 2,

    /// <summary>Replays missed events after a dropped connection.</summary>
    Resume = 6,

    /// <summary>Discord asking for a reconnect; the session may be resumed.</summary>
    Reconnect = 7,

    /// <summary>The session is gone and must be identified afresh.</summary>
    InvalidSession = 9,

    /// <summary>First frame on any connection; carries the heartbeat interval.</summary>
    Hello = 10,

    /// <summary>Acknowledges a heartbeat. Silence here means a dead socket.</summary>
    HeartbeatAck = 11,
}

/// <summary>One decoded gateway frame.</summary>
public sealed record GatewayFrame(DiscordOpcode Opcode, int? Sequence, string? EventName, JsonElement Data);

/// <summary>A message Discord pushed at us, before any policy is applied.</summary>
public sealed record DiscordMessage(
    string MessageId,
    string ChannelId,
    string GuildId,
    string AuthorId,
    bool AuthorIsBot,
    string Content);

/// <summary>Reads and writes the gateway wire format.</summary>
/// <remarks>
/// Pure static functions over JSON, separate from the socket, because the protocol is
/// where the subtle mistakes live and a socket is a poor place to test them. D-013 took
/// the same position about hand-rolled framing and was explicit that the happy path is
/// the cheap part.
/// </remarks>
public static class DiscordGatewayProtocol
{
    /// <summary>
    /// Intents this gateway asks for: guild messages, direct messages, and the privileged
    /// message content.
    /// </summary>
    /// <remarks>
    /// Message content is privileged and must be enabled in the developer portal. Without
    /// it the socket connects, events arrive, and every message body is empty — a failure
    /// that looks like silence rather than an error, which is why it is named here.
    /// </remarks>
    public const int INTENTS = (1 << 9) | (1 << 12) | (1 << 15);

    /// <summary>Decodes a frame, or null if it is not one.</summary>
    public static GatewayFrame? ReadFrame(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (json.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            return new GatewayFrame(
                (DiscordOpcode)op.GetInt32(),
                Sequence(root),
                Name(root),
                Payload(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Sequence(JsonElement root) =>
        root.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32()
            : null;

    private static string? Name(JsonElement root) =>
        root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

    private static JsonElement Payload(JsonElement root) =>
        root.TryGetProperty("d", out var d) ? d.Clone() : default;

    /// <summary>The heartbeat interval HELLO carries, or null if the frame is malformed.</summary>
    public static TimeSpan? ReadHeartbeatInterval(GatewayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Opcode != DiscordOpcode.Hello
            || !frame.Data.TryGetProperty("heartbeat_interval", out var interval)
            || interval.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var milliseconds = interval.GetDouble();
        return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : null;
    }

    /// <summary>Reads a MESSAGE_CREATE dispatch, or null if the frame is anything else.</summary>
    public static DiscordMessage? ReadMessage(GatewayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Opcode != DiscordOpcode.Dispatch
            || !string.Equals(frame.EventName, "MESSAGE_CREATE", StringComparison.Ordinal)
            || frame.Data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var author = frame.Data.TryGetProperty("author", out var a) ? a : default;
        return new DiscordMessage(
            Text(frame.Data, "id"),
            Text(frame.Data, "channel_id"),
            Text(frame.Data, "guild_id"),
            Text(author, "id"),
            author.ValueKind == JsonValueKind.Object
                && author.TryGetProperty("bot", out var bot)
                && bot.ValueKind == JsonValueKind.True,
            Text(frame.Data, "content"));
    }

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    /// <summary>Builds the IDENTIFY frame that authenticates a fresh connection.</summary>
    public static string Identify(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return JsonSerializer.Serialize(new
        {
            op = (int)DiscordOpcode.Identify,
            d = new
            {
                token,
                intents = INTENTS,
                properties = new { os = "linux", browser = "dami", device = "dami" },
            },
        });
    }

    /// <summary>Builds a heartbeat carrying the last sequence number seen.</summary>
    public static string Heartbeat(int? lastSequence) =>
        JsonSerializer.Serialize(new { op = (int)DiscordOpcode.Heartbeat, d = lastSequence });

    /// <summary>Builds a RESUME frame, which replays what was missed.</summary>
    public static string Resume(string token, string sessionId, int lastSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return JsonSerializer.Serialize(new
        {
            op = (int)DiscordOpcode.Resume,
            d = new { token, session_id = sessionId, seq = lastSequence },
        });
    }
}
