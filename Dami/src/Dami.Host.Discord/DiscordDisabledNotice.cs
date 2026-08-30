using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Says out loud that the Discord gateway is not running, and what is missing.</summary>
/// <remarks>
/// Registered in place of the worker when the gateway is unconfigured. Without it the
/// composition simply registers nothing and the host starts in complete silence, which is
/// indistinguishable from a working gateway that has nothing to say — that ambiguity cost
/// a live debugging session on 2026-08-30, where an empty token file looked exactly like
/// a healthy start.
/// </remarks>
public sealed class DiscordDisabledNotice : BackgroundService
{
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordDisabledNotice> logger;

    /// <summary>Creates the notice.</summary>
    public DiscordDisabledNotice(DiscordOptions options, ILogger<DiscordDisabledNotice> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogWarning(
            "Discord gateway is NOT running. Enabled={Enabled}, token={Token}, owner={Owner}. "
            + "The token comes from Discord__Token in /etc/dami/discord.env.",
            this.options.Enabled,
            this.options.Token.Length > 0 ? $"present ({this.options.Token.Length} chars)" : "MISSING",
            this.options.OwnerUserId.Length > 0 ? "set" : "MISSING");

        return Task.CompletedTask;
    }
}
