namespace Dami.Gateway.Discord;

/// <summary>How to reach Discord, and who is allowed to talk to it.</summary>
/// <remarks>
/// The token is supplied by the systemd drop-in through <c>Discord__Token</c> and is
/// never in the repository, in appsettings, or in a trace. The owner id matters as much:
/// a bot in a server is addressable by everyone in it, and without it any member could
/// drive the runtime.
/// </remarks>
public sealed class DiscordOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "Discord";

    /// <summary>Bot token. Empty disables the gateway entirely.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The only user whose messages are acted on.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>The guild the bot is expected in. Empty allows any.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Whether the gateway should run at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Prior exchanges carried into a turn. The window is bounded on purpose.</summary>
    public int HistoryTurns { get; set; } = 6;

    /// <summary>Maximum time one inbound image may occupy the shared vision sidecar.</summary>
    public TimeSpan VisionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to refresh Discord's expiring typing state.</summary>
    public TimeSpan TypingRefresh { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The conversation (Steve's DM channel id) the once-a-day check-in goes to. Empty
    /// turns the check-in off (ADR-0014 as amended 2026-09-29).
    /// </summary>
    public string CheckInConversationId { get; set; } = string.Empty;

    /// <summary>The local hour from which the day's check-in may be sent.</summary>
    public int CheckInHour { get; set; } = 9;

    /// <summary>The time zone <see cref="CheckInHour"/> is read in.</summary>
    public string CheckInTimeZone { get; set; } = "America/Chicago";

    /// <summary>How often the check-in looks at the clock.</summary>
    public TimeSpan CheckInPoll { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether the options are complete enough to connect.</summary>
    public bool IsConfigured =>
        this.Enabled && this.Token.Length > 0 && this.OwnerUserId.Length > 0;
}
