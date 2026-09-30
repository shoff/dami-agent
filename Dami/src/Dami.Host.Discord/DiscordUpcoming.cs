using System.Globalization;
using System.Text;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Runtime;
using Dami.Contracts.Scheduling;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>"next" / "upcoming": what Dami will do on her own over the coming week, in order (H7).</summary>
/// <remarks>
/// Meta Muse's audit trail shows planned actions, not only done ones; an agent that acts
/// unprompted should say what it is about to do. Read from the job store and the run history;
/// operational content, no model.
/// </remarks>
public sealed class DiscordUpcoming : IDiscordCommand
{
    private const int SHOWN = 15;

    private readonly IScheduledJobStore jobs;
    private readonly IProactiveRunHistory history;
    private readonly IPauseSwitch pause;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordUpcoming> logger;

    /// <summary>Creates the responder.</summary>
    public DiscordUpcoming(
        IScheduledJobStore jobs, IProactiveRunHistory history, IPauseSwitch pause, IEgressChannel channel,
        DiscordOptions options, TimeProvider clock, ILogger<DiscordUpcoming> logger)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.jobs, this.history, this.pause, this.channel) = (jobs, history, pause, channel);
        (this.options, this.clock, this.logger) = (options, clock, logger);
    }

    /// <summary>Answers when the message is exactly "next" or "upcoming".</summary>
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Text.Trim().TrimStart('!', '/').Trim().ToLowerInvariant() is not ("next" or "upcoming"))
        {
            return false;
        }

        var now = this.clock.GetUtcNow();
        var text = new StringBuilder();
        if (await this.pause.CurrentAsync(now, cancellationToken).ConfigureAwait(false) is { } paused)
        {
            text.Append("⏸️ Paused ").Append(paused.Until is { } until ? "until " + this.When(until, now) : "until `resume`")
                .Append(": none of this runs until then.\n");
        }

        text.Append("**What I will do on my own, next 7 days:**");
        foreach (var (what, when) in (await this.PlannedAsync(now, cancellationToken).ConfigureAwait(false)).Take(SHOWN))
        {
            text.Append("\n• ").Append(what).Append(" — ").Append(when <= now ? "due now" : this.When(when, now));
        }

        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, text.ToString(), ContentProvenance.Operational, Guid.NewGuid()), cancellationToken)
            .ConfigureAwait(false);
        this.logger.LogInformation("Upcoming shown");
        return true;
    }

    private async Task<List<(string What, DateTimeOffset When)>> PlannedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var planned = (await this.jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Status == ScheduledJobStatus.Active && job.NextRunAt is not null)
            .Select(job => ($"job \"{job.Name}\"", job.NextRunAt!.Value))
            .ToList();
        planned.AddRange((await this.history.ReadAsync(1, cancellationToken).ConfigureAwait(false))
            .Where(service => service.NextDueAt is not null)
            .Select(service => (service.ServiceName, service.NextDueAt!.Value)));
        if (this.options.CheckInConversationId.Length > 0)
        {
            var today = CheckInClock.DueToday(this.options, now);
            planned.Add(("daily check-in", today > now ? today : today.AddDays(1)));
        }

        return [.. planned.Where(item => item.Item2 <= now.AddDays(7)).OrderBy(item => item.Item2)];
    }

    private string When(DateTimeOffset at, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(this.options.CheckInTimeZone);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        return local.Date == TimeZoneInfo.ConvertTime(now, zone).Date
            ? local.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : local.ToString("ddd h:mm tt", CultureInfo.InvariantCulture);
    }
}
