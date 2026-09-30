using System.Text;
using Dami.Contracts.Privacy;
using Dami.Core.Reliability;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>
/// From the check-in hour, anything that went wrong since yesterday goes to Steve's DM;
/// on Sundays, the week summed up whether or not anything did.
/// </summary>
/// <remarks>
/// A smoke detector, not a muse (ADR-0014 §2): service names and error text only, nothing
/// from the profile, so it is operational content and needs no disclosure gate. A clean
/// weekday says nothing. Once a day per process; a restart after the hour on a day with
/// problems says them again, which is the right side to err on.
/// </remarks>
public sealed class DiscordReliabilityNotice : BackgroundService
{
    private readonly IReliabilityReport report;
    private readonly WeeklyActivity activity;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordReliabilityNotice> logger;
    private readonly DayMarker marker;

    /// <summary>Creates the notice, remembering the last day it spoke under <c>~/.local/state/dami</c>.</summary>
    public DiscordReliabilityNotice(
        IReliabilityReport report,
        WeeklyActivity activity,
        IEgressChannel channel,
        DiscordOptions options,
        TimeProvider clock,
        ILogger<DiscordReliabilityNotice> logger)
        : this(report, activity, channel, options, clock, DayMarker.Default("reliability-notice"), logger)
    {
    }

    /// <summary>Creates the notice with the file that remembers the last day it spoke.</summary>
    /// <remarks>
    /// A file, not memory: every deploy restarts the host, and on 2026-09-29 that would
    /// have repeated the day's list after each one.
    /// </remarks>
    public DiscordReliabilityNotice(
        IReliabilityReport report,
        WeeklyActivity activity,
        IEgressChannel channel,
        DiscordOptions options,
        TimeProvider clock,
        string statePath,
        ILogger<DiscordReliabilityNotice> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(logger);
        this.marker = new DayMarker(statePath, logger);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.report = report;
        this.activity = activity;
        this.channel = channel;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Speaks today's notice if it is due and there is something to say.</summary>
    /// <returns>True when a notice was sent on this tick.</returns>
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var due = CheckInClock.DueToday(this.options, now);
        if (this.options.CheckInConversationId.Length == 0 || now < due || this.marker.Read() >= due)
        {
            return false;
        }

        this.marker.Write(due);
        var weekly = CheckInClock.Today(this.options, now) == DayOfWeek.Sunday;
        var reading = await this.report.ReadAsync(now.AddDays(weekly ? -7 : -1), cancellationToken).ConfigureAwait(false);
        if (!weekly && reading.Problems.Count == 0)
        {
            return false;
        }

        var text = Text(reading, weekly);
        if (weekly)
        {
            text += "\n" + await this.activity.LineAsync(now.AddDays(-7), cancellationToken).ConfigureAwait(false);
        }

        await this.channel.SendAsync(
            new OutboundContent(this.options.CheckInConversationId, text, ContentProvenance.Operational, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Reliability notice sent: {Count} problem(s): {Problems}", reading.Problems.Count, string.Join("; ", reading.Problems));
        return true;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.logger.LogWarning(exception, "Reliability notice tick failed");
            }

            await Task.Delay(this.options.CheckInPoll, this.clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private static string Text(ReliabilityReading reading, bool weekly)
    {
        var text = new StringBuilder();
        text.Append(weekly
            ? $"🛠 This week: {reading.Passes} proactive passes, {reading.FailedPasses} failed"
            : "🛠 Since yesterday, something went wrong without anyone noticing");
        if (reading.Problems.Count == 0)
        {
            return text.Append("; nothing else went wrong.").ToString();
        }

        text.Append(':');
        foreach (var problem in reading.Problems)
        {
            text.Append("\n• ").Append(problem);
        }

        return text.ToString();
    }
}
