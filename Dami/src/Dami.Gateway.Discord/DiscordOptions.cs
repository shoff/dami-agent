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

    /// <summary>
    /// Whether the frontier answers on locally-assembled context (ADR-0026). False makes
    /// the local sidecar answer directly again, which is that decision's reversal path.
    /// </summary>
    public bool Frontier { get; set; } = true;

    /// <summary>Prior exchanges carried into a turn. The window is bounded on purpose.</summary>
    public int HistoryTurns { get; set; } = 6;

    /// <summary>Whether the options are complete enough to connect.</summary>
    public bool IsConfigured =>
        this.Enabled && this.Token.Length > 0 && this.OwnerUserId.Length > 0;
}
