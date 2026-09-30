using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Privacy;
using Dami.Contracts.Runtime;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>"pause", "pause 3h", "pause 2d", "resume" — the switch for everything Dami starts on her own (A10).</summary>
/// <remarks>
/// Paused: no proactive pass, no scheduled job, no check-in. Dami still answers, and the
/// outage alarm still watches — a smoke detector is not something to switch off from chat.
/// Deliberately exact: "should I pause the build" is a question, not a command.
/// </remarks>
public sealed partial class DiscordPauseCommand : IDiscordCommand
{
    private const string REASON = "paused from Discord";

    private readonly IPauseSwitch pause;
    private readonly IEgressChannel channel;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordPauseCommand> logger;

    /// <summary>Creates the command.</summary>
    public DiscordPauseCommand(IPauseSwitch pause, IEgressChannel channel, TimeProvider clock, ILogger<DiscordPauseCommand> logger)
    {
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.pause, this.channel, this.clock, this.logger) = (pause, channel, clock, logger);
    }

    /// <summary>Handles the message when it is exactly a pause or resume command.</summary>
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Text.Trim().TrimStart('!', '/').Trim();
        string reply;
        if (text.Equals("resume", StringComparison.OrdinalIgnoreCase))
        {
            await this.pause.ResumeAsync(cancellationToken).ConfigureAwait(false);
            reply = "▶️ Resumed: proactive passes, scheduled jobs and the check-in run again.";
        }
        else if (Command().Match(text) is { Success: true } match)
        {
            var now = this.clock.GetUtcNow();
            var until = match.Groups["n"].Success ? now + Span(match) : (DateTimeOffset?)null;
            await this.pause.PauseAsync(until, REASON, now, cancellationToken).ConfigureAwait(false);
            reply = "⏸️ Paused " + (until is null ? "until you say `resume`" : $"for {match.Groups["n"].Value}{match.Groups["u"].Value}")
                + ": no proactive passes, scheduled jobs or check-in. I still answer, and the outage alarm still watches. `resume` to lift it.";
        }
        else
        {
            return false;
        }

        this.logger.LogInformation("Pause switch: {Reply}", reply);
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, reply, ContentProvenance.Operational, Guid.NewGuid()), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static TimeSpan Span(Match match)
    {
        var n = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        return match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "m" => TimeSpan.FromMinutes(n),
            "h" => TimeSpan.FromHours(n),
            _ => TimeSpan.FromDays(n),
        };
    }

    [GeneratedRegex(@"^pause(\s+(?<n>\d{1,3})(?<u>[mhd]))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Command();
}
