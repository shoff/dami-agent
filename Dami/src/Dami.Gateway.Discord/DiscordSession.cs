namespace Dami.Gateway.Discord;

/// <summary>What must survive a dropped connection for the next one to resume it.</summary>
/// <remarks>
/// Separate and testable because resume correctness is invisible until it is wrong: a
/// gateway that silently re-identifies instead of resuming looks perfectly healthy while
/// dropping every message sent during the gap.
/// </remarks>
public sealed class DiscordSession
{
    /// <summary>The resumable session id from READY, if one has been seen.</summary>
    public string? SessionId { get; private set; }

    /// <summary>The last sequence number Discord sent, which RESUME replays from.</summary>
    public int? LastSequence { get; private set; }

    /// <summary>The bot's own user id, so its own messages can be ignored.</summary>
    public string SelfId { get; private set; } = string.Empty;

    /// <summary>
    /// Where a RESUME must be sent. Discord hands this out in READY and rejects a resume
    /// aimed anywhere else, which presents as an endless identify/invalid-session loop
    /// rather than as an error.
    /// </summary>
    public Uri? ResumeGateway { get; private set; }

    /// <summary>Whether the next connection may resume rather than identify afresh.</summary>
    public bool CanResume => this.SessionId is { Length: > 0 } && this.LastSequence is not null;

    /// <summary>Records what a frame reveals about the session.</summary>
    public void Observe(GatewayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Sequence is { } sequence)
        {
            this.LastSequence = sequence;
        }

        if (frame.Opcode == DiscordOpcode.Dispatch
            && string.Equals(frame.EventName, "READY", StringComparison.Ordinal))
        {
            this.ReadReady(frame);
        }
    }

    private void ReadReady(GatewayFrame frame)
    {
        if (frame.Data.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        if (frame.Data.TryGetProperty("session_id", out var session)
            && session.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            this.SessionId = session.GetString();
        }

        if (frame.Data.TryGetProperty("resume_gateway_url", out var resume)
            && resume.ValueKind == System.Text.Json.JsonValueKind.String
            && Uri.TryCreate(resume.GetString(), UriKind.Absolute, out var url))
        {
            this.ResumeGateway = url;
        }

        if (frame.Data.TryGetProperty("user", out var user)
            && user.ValueKind == System.Text.Json.JsonValueKind.Object
            && user.TryGetProperty("id", out var id)
            && id.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            this.SelfId = id.GetString() ?? string.Empty;
        }
    }

    /// <summary>Forgets the session, so the next connection identifies afresh.</summary>
    public void Invalidate()
    {
        this.SessionId = null;
        this.LastSequence = null;
        this.ResumeGateway = null;
    }
}
