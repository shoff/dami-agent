using System.Globalization;
using System.Text;
using Dami.Contracts.Anticoag;
using Dami.Contracts.Privacy;
using Dami.Core.Anticoag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Host.Discord;

/// <summary>Steve's INR target range, set by him (ADR-0037 rule 6).</summary>
public sealed class AnticoagOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "Anticoag";

    /// <summary>Lower end of the clinic's target range; null means no range line at all.</summary>
    public decimal? TargetLow { get; set; }

    /// <summary>Upper end of the clinic's target range.</summary>
    public decimal? TargetHigh { get; set; }

    /// <summary>The zone "today" is read in.</summary>
    public string TimeZone { get; set; } = "America/Chicago";
}

/// <summary>"INR 2.4" and dose changes, logged on this host and answered in Steve's DM (ADR-0037).</summary>
/// <remarks>
/// Runs before the frontier, which never sees a reading or a dose. The reply places the
/// reading against his range and his own recent readings; outside the range it says the
/// clinic decides, and above it names the label's urgent signs. It never proposes a dose.
/// </remarks>
public sealed class DiscordAnticoag : IDiscordCommand
{
    private const int TREND = 3;

    private readonly IAnticoagLog log;
    private readonly IEgressChannel channel;
    private readonly AnticoagOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordAnticoag> logger;

    /// <summary>Creates the command.</summary>
    public DiscordAnticoag(
        IAnticoagLog log, IEgressChannel channel, IOptions<AnticoagOptions> options, TimeProvider clock, ILogger<DiscordAnticoag> logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.log, this.channel, this.options, this.clock, this.logger) = (log, channel, options.Value, clock, logger);
    }

    /// <inheritdoc />
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(message.ReceivedAt, TimeZoneInfo.FindSystemTimeZoneById(this.options.TimeZone)).Date);
        string? reply = null;
        if (AnticoagCapture.Reading(message.Text, today) is { } reading)
        {
            reply = await this.ReadingAsync(reading.Inr, reading.OnDay, cancellationToken).ConfigureAwait(false);
        }
        else if (AnticoagCapture.DoseChange(message.Text) is { } dose)
        {
            await this.log.RecordAsync(new DoseChange(Guid.NewGuid(), today, dose, this.clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            reply = $"Dose noted for {Day(today)}, as you wrote it: “{dose}”. I keep it beside your INR history; "
                + "I never suggest doses — that stays with your clinic.";
        }

        if (reply is null)
        {
            return false;
        }

        this.logger.LogInformation("Anticoagulation entry logged locally");
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, reply, ContentProvenance.ProfileDerived, Guid.NewGuid()), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<string> ReadingAsync(decimal inr, DateOnly onDay, CancellationToken cancellationToken)
    {
        await this.log.RecordAsync(new InrReading(Guid.NewGuid(), onDay, inr, this.clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        var earlier = (await this.log.ReadingsAsync(TREND + 3, cancellationToken).ConfigureAwait(false))
            .Where(item => item.OnDay < onDay).Take(TREND).ToList();
        var said = new StringBuilder($"INR {Value(inr)} logged for {Day(onDay)}");
        said.Append(this.Range(inr));
        if (earlier.Count > 0)
        {
            said.Append(" Before: ").AppendJoin(", ", earlier.Select(item => $"{Value(item.Inr)} ({Day(item.OnDay)})"))
                .Append(CultureInfo.InvariantCulture, $". {onDay.DayNumber - earlier[0].OnDay.DayNumber} days since the reading before.");
        }

        return said.ToString();
    }

    private string Range(decimal inr)
    {
        if (this.options is not { TargetLow: { } low, TargetHigh: { } high })
        {
            return ".";
        }

        var range = $"{Value(low)}–{Value(high)}";
        return inr < low
            ? $" — below your {range} range. Your anticoagulation clinic decides any change; call them if they have not seen this result."
            : inr > high
                ? $" — above your {range} range. Your anticoagulation clinic decides any change; call them if they have not seen this result. "
                  + "Unusual bleeding, black or bloody stools, or a sudden severe headache are urgent: call 911."
                : $" — within your {range} range.";
    }

    private static string Value(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("MMM d", CultureInfo.InvariantCulture);
}

/// <summary>A warfarin interactor in what Steve says, noticed in his DM as a prompt to ask (ADR-0037 rule 3).</summary>
/// <remarks>
/// A side note: the message is still answered as usual. Each interactor is said at most
/// once a week, so a course of antibiotics is one note, not one per message.
/// </remarks>
public sealed class DiscordInteractionWatch
{
    private static readonly TimeSpan quiet = TimeSpan.FromDays(7);

    private readonly IEgressChannel channel;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordInteractionWatch> logger;
    private readonly Dictionary<string, DateTimeOffset> said = new(StringComparer.Ordinal);

    /// <summary>Creates the watch.</summary>
    public DiscordInteractionWatch(IEgressChannel channel, TimeProvider clock, ILogger<DiscordInteractionWatch> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.channel, this.clock, this.logger) = (channel, clock, logger);
    }

    /// <summary>Notes an interactor named in the message, if any and not said this week.</summary>
    public Task NoticeAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this.NoticeAsync(message.ConversationId, message.Text, null, cancellationToken);
    }

    /// <summary>
    /// Notes an interactor named in <paramref name="text"/>, seen <paramref name="where"/> (null: in
    /// what Steve said), if not said this week.
    /// </summary>
    public async Task NoticeAsync(string conversationId, string text, string? where, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var now = this.clock.GetUtcNow();
        if (AnticoagCapture.Interactor(text) is not { } found
            || (this.said.TryGetValue(found.Name, out var last) && now - last < quiet))
        {
            return;
        }

        this.said[found.Name] = now;
        this.logger.LogInformation("Warfarin interactor noticed");
        await this.channel.SendAsync(
            new OutboundContent(
                conversationId,
                $"💊 Noticed{(where is null ? string.Empty : " " + where)}: {found.Name} {found.Effect} with warfarin. It's worth "
                + "asking whoever prescribed it whether your INR should be checked sooner. (A prompt to ask, not advice; the list I "
                + "watch is not exhaustive.)",
                ContentProvenance.ProfileDerived, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
    }
}
