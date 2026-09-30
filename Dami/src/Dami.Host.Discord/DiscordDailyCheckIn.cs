using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>
/// Once a day, from <see cref="DiscordOptions.CheckInHour"/>, the single strongest pending
/// surfacing goes to Steve's DM as a frontier turn (ADR-0014 as amended 2026-09-29).
/// </summary>
/// <remarks>
/// The rest stay in the queue, riding his next message as before, and so do surfacings
/// from <see cref="DiscordOptions.CheckInExcludedServices"/>. A day with nothing
/// pending sends nothing. The push is recorded on the surfacing, so the day's check-in
/// survives a restart and H8's tuner leaves the reaction to it out. A failed attempt is
/// explained in the DM once and not retried until the next day: every poll would
/// otherwise add another apology.
/// </remarks>
public sealed class DiscordDailyCheckIn : BackgroundService
{
    /// <summary>How a check-in delivers, as recorded on the surfacing.</summary>
    public const string VIA = "discord-dm";

    private const int PENDING_LIMIT = 50;

    private readonly DiscordAnswerer answerer;
    private readonly ISurfacingQueue surfacings;
    private readonly ISpeechClient speech;
    private readonly IEgressChannel channel;
    private readonly Dami.Contracts.Runtime.IPauseSwitch pause;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordDailyCheckIn> logger;
    private DateTimeOffset attemptedFor = DateTimeOffset.MinValue;

    /// <summary>Creates the check-in.</summary>
    public DiscordDailyCheckIn(
        DiscordAnswerer answerer,
        ISurfacingQueue surfacings,
        ISpeechClient speech,
        IEgressChannel channel,
        Dami.Contracts.Runtime.IPauseSwitch pause,
        DiscordOptions options,
        TimeProvider clock,
        ILogger<DiscordDailyCheckIn> logger)
    {
        ArgumentNullException.ThrowIfNull(answerer);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.answerer = answerer;
        this.surfacings = surfacings;
        this.speech = speech;
        this.channel = channel;
        this.pause = pause;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Sends today's check-in if it is due and there is something to say.</summary>
    /// <returns>True when a check-in reached Discord on this tick.</returns>
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var due = CheckInClock.DueToday(this.options, this.clock.GetUtcNow());
        if (!await this.IsDueAsync(due, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var strongest = await this.StrongestAsync(cancellationToken).ConfigureAwait(false);
        if (strongest is null)
        {
            return false;
        }

        this.attemptedFor = due;
        var outcome = await this.answerer
            .CheckInAsync(this.options.CheckInConversationId, strongest, VIA, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Daily check-in with {Service} \"{Title}\": {Outcome}",
            strongest.ServiceName, strongest.Title, outcome.Failure ?? "sent");
        if (outcome.Answer is not null && this.options.CheckInVoice)
        {
            await this.SpeakAsync(outcome.Answer, cancellationToken).ConfigureAwait(false);
        }

        return outcome.IsAnswered;
    }

    /// <summary>
    /// The check-in's own words, read by the local voice and attached. Nothing new leaves
    /// the host: the text already went to the same conversation. A voice that fails costs
    /// only the voice.
    /// </summary>
    private async Task SpeakAsync(string answer, CancellationToken cancellationToken)
    {
        try
        {
            var audio = await this.speech.SpeakAsync(answer, cancellationToken).ConfigureAwait(false);
            await this.channel.SendAsync(
                new OutboundContent(this.options.CheckInConversationId, string.Empty, ContentProvenance.Operational, Guid.NewGuid())
                {
                    Attachments = [new OutboundAttachment("check-in.wav", audio, "audio/wav")],
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "The check-in went out; its voice did not");
        }
    }

    /// <summary>Configured, past the hour, not yet tried or sent today, and not paused (A10).</summary>
    private async Task<bool> IsDueAsync(DateTimeOffset due, CancellationToken cancellationToken) =>
        this.options.CheckInConversationId.Length > 0
        && this.clock.GetUtcNow() >= due
        && this.attemptedFor < due
        && await this.pause.CurrentAsync(this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false) is null
        && !(await this.surfacings.LastPushedAtAsync(VIA, cancellationToken).ConfigureAwait(false) >= due);

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
                this.logger.LogWarning(exception, "Daily check-in tick failed");
            }

            await Task.Delay(this.options.CheckInPoll, this.clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<Surfacing?> StrongestAsync(CancellationToken cancellationToken)
    {
        Surfacing? strongest = null;
        await foreach (var surfacing in this.surfacings.PendingAsync(PENDING_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            var excluded = this.options.CheckInExcludedServices.Contains(surfacing.ServiceName, StringComparer.OrdinalIgnoreCase);
            if (!excluded && (strongest is null || surfacing.Confidence > strongest.Confidence))
            {
                strongest = surfacing;
            }
        }

        return strongest;
    }
}
