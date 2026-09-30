using System.Text;
using Dami.Contracts.Anticoag;
using Dami.Contracts.Nutrition;
using Dami.Contracts.Privacy;
using Dami.Contracts.Runtime;
using Dami.Core.Anticoag;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Mondays from the check-in hour: the anticoagulation note, in Steve's DM (ADR-0037 slice 2).</summary>
/// <remarks>
/// Rendered on this host from his own readings, doses and meals; ProfileDerived, so the channel
/// refuses it anywhere but his DM. Silent when nothing is logged or while paused. Once a week,
/// across restarts.
/// </remarks>
public sealed class DiscordAnticoagWeekly : BackgroundService
{
    private readonly IAnticoagLog log;
    private readonly IMealLog meals;
    private readonly IPauseSwitch pause;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordAnticoagWeekly> logger;
    private readonly DayMarker marker;

    /// <summary>Creates the note, remembering its week under <c>~/.local/state/dami</c>.</summary>
    public DiscordAnticoagWeekly(
        IAnticoagLog log, IMealLog meals, IPauseSwitch pause, IEgressChannel channel, DiscordOptions options,
        TimeProvider clock, ILogger<DiscordAnticoagWeekly> logger)
        : this(log, meals, pause, channel, options, clock, DayMarker.Default("anticoag-weekly"), logger)
    {
    }

    /// <summary>Creates the note with the file that remembers its week.</summary>
    public DiscordAnticoagWeekly(
        IAnticoagLog log, IMealLog meals, IPauseSwitch pause, IEgressChannel channel, DiscordOptions options,
        TimeProvider clock, string statePath, ILogger<DiscordAnticoagWeekly> logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(meals);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.log, this.meals, this.pause, this.channel) = (log, meals, pause, channel);
        (this.options, this.clock, this.logger) = (options, clock, logger);
        this.marker = new DayMarker(statePath, logger);
    }

    /// <summary>Sends this week's note if it is Monday past the hour and there is something to say.</summary>
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var due = CheckInClock.DueToday(this.options, now);
        if (!await this.IsDueAsync(now, due, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        this.marker.Write(due);
        var lines = AnticoagWeek.Lines(
            await this.log.ReadingsAsync(20, cancellationToken).ConfigureAwait(false),
            await this.log.DosesAsync(5, cancellationToken).ConfigureAwait(false),
            await this.meals.BetweenAsync(now.AddDays(-36), now, cancellationToken).ConfigureAwait(false),
            now);
        if (lines.Count == 0)
        {
            return false;
        }

        var text = new StringBuilder("💉 Anticoagulation, this week:");
        foreach (var line in lines)
        {
            text.Append("\n• ").Append(line);
        }

        await this.channel.SendAsync(
            new OutboundContent(this.options.CheckInConversationId, text.ToString(), ContentProvenance.ProfileDerived, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Anticoagulation note sent: {Count} line(s)", lines.Count);
        return true;
    }

    /// <summary>Configured, Monday, past the hour, not yet sent this week, and not paused.</summary>
    private async Task<bool> IsDueAsync(DateTimeOffset now, DateTimeOffset due, CancellationToken cancellationToken) =>
        this.options.CheckInConversationId.Length > 0
        && CheckInClock.Today(this.options, now) == DayOfWeek.Monday
        && now >= due
        && this.marker.Read() < due
        && await this.pause.CurrentAsync(now, cancellationToken).ConfigureAwait(false) is null;

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
                this.logger.LogWarning(exception, "Anticoagulation note tick failed");
            }

            await Task.Delay(this.options.CheckInPoll, this.clock, stoppingToken).ConfigureAwait(false);
        }
    }
}
