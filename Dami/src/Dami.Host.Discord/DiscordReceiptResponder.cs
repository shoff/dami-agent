using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Finance;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Finance;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>A photo Steve calls a receipt becomes an expense, entirely on this host.</summary>
/// <remarks>
/// Receipt photo → ledger was the most repeated durable finance habit in the 2026-09-29
/// survey (docs/agent-landscape-2026-09.md D4). The disclosure gate withholds financial
/// detail from the frontier, so the frontier never takes part: the local vision model reads
/// the receipt, the ledger keeps it, and the confirmation goes to Steve's own DM — the
/// only place ADR-0025 lets profile-derived content go.
/// </remarks>
public sealed partial class DiscordReceiptResponder
{
    private static readonly CultureInfo money = CultureInfo.GetCultureInfo("en-US");

    private readonly IVisionClient vision;
    private readonly IDiscordRest rest;
    private readonly IExpenseLedger ledger;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordReceiptResponder> logger;

    /// <summary>Creates the responder.</summary>
    public DiscordReceiptResponder(
        IVisionClient vision,
        IDiscordRest rest,
        IExpenseLedger ledger,
        IEgressChannel channel,
        DiscordOptions options,
        TimeProvider clock,
        ILogger<DiscordReceiptResponder> logger)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.vision = vision;
        this.rest = rest;
        this.ledger = ledger;
        this.channel = channel;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Logs the receipt when the message is a photo Steve calls a receipt.</summary>
    /// <returns>False when the message is not one, so something else answers it.</returns>
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var photo = message.Attachments.FirstOrDefault(attachment => attachment.IsImage);
        if (photo is null || !ReceiptWord().IsMatch(message.Text))
        {
            return false;
        }

        var sentOn = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(message.ReceivedAt, TimeZoneInfo.FindSystemTimeZoneById(this.options.CheckInTimeZone)).Date);
        var reply = await this.ReadAsync(photo, cancellationToken).ConfigureAwait(false);
        var expense = reply is null ? null : ReceiptReader.Read(reply, sentOn, this.clock.GetUtcNow());
        var text = expense is null
            ? "I couldn't read a merchant and a total on that receipt. A flatter, closer photo usually works."
            : await this.RecordAsync(expense, sentOn, cancellationToken).ConfigureAwait(false);
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, text, ContentProvenance.ProfileDerived, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<string?> ReadAsync(InboundAttachment photo, CancellationToken cancellationToken)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(this.options.VisionTimeout * 2);
            var bytes = await this.rest.DownloadAsync(photo.Url, budget.Token).ConfigureAwait(false);
            return await this.vision.DescribeAsync(bytes, ReceiptReader.PROMPT, budget.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(exception, "Could not read receipt {File}", photo.FileName);
            return null;
        }
    }

    private async Task<string> RecordAsync(Expense expense, DateOnly sentOn, CancellationToken cancellationToken)
    {
        var line = $"{expense.Total.ToString("C", money)} at {expense.Merchant} ({expense.Category}, {expense.SpentOn:MMM d})";
        if (!await this.ledger.RecordAsync(expense, cancellationToken).ConfigureAwait(false))
        {
            return $"Already logged: {line}.";
        }

        // The month the receipt belongs to, to date: through today, or through its last day
        // when the receipt is from an earlier month.
        var month = new DateOnly(expense.SpentOn.Year, expense.SpentOn.Month, 1);
        var through = sentOn < month.AddMonths(1) ? sentOn : month.AddMonths(1).AddDays(-1);
        var spent = await this.ledger.BetweenAsync(month, through, cancellationToken).ConfigureAwait(false);
        var total = spent.Where(item => item.Category == expense.Category).Sum(item => item.Total);
        this.logger.LogInformation("Receipt logged locally: {Category}", expense.Category);
        return $"Logged {line}. {char.ToUpperInvariant(expense.Category[0])}{expense.Category[1..]} this month: {total.ToString("C", money)}.";
    }

    [GeneratedRegex(@"\breceipts?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptWord();
}
