using System.Globalization;
using System.Text;
using Dami.Contracts.Domains;
using Dami.Contracts.Finance;
using Dami.Contracts.Mail;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive.Mail;

/// <summary>Files what Steve forwards to Dami's own mailbox (docs/agent-landscape-2026-09.md D2).</summary>
/// <remarks>
/// The durable email pattern in the 2026-09-29 survey is not giving an agent your inbox but
/// forwarding things to its own. Each new message is read by the local model — no tools, so
/// an injected instruction has nothing to act with — into one structured item: a receipt goes
/// to the expense ledger, anything else lands on the "mail" timeline on its own date. Only
/// that reading is kept; the body never is. One note per pass says what was filed.
/// </remarks>
public sealed class MailboxCollectorService : IProactiveService
{
    private const string DOMAIN = "mail";
    private const int BODY_CHARS = 5000;
    private const int PER_PASS = 20;
    private static readonly TimeSpan window = TimeSpan.FromDays(7);

    private readonly IMailbox mailbox;
    private readonly IMailLedger ledger;
    private readonly IChatClient local;
    private readonly IExpenseLedger expenses;
    private readonly IDomainFactStore facts;
    private readonly TimeProvider clock;
    private readonly ILogger<MailboxCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public MailboxCollectorService(
        IMailbox mailbox,
        IMailLedger ledger,
        IChatClient local,
        IExpenseLedger expenses,
        IDomainFactStore facts,
        TimeProvider clock,
        ILogger<MailboxCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.mailbox = mailbox;
        this.ledger = ledger;
        this.local = local;
        this.expenses = expenses;
        this.facts = facts;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "mailbox-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.EightHourly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!this.mailbox.IsConfigured)
        {
            return ProactiveResult.Did("no mailbox configured (Mailbox__User, Mailbox__Password)");
        }

        var mail = await this.mailbox.RecentAsync(window, cancellationToken).ConfigureAwait(false);
        var filed = await this.ledger.FiledAsync([.. mail.Select(item => item.MessageId)], cancellationToken).ConfigureAwait(false);
        var summaries = new List<string>();
        foreach (var message in mail.Where(item => !filed.Contains(item.MessageId)).Take(PER_PASS))
        {
            summaries.Add(await this.FileAsync(message, cancellationToken).ConfigureAwait(false));
        }

        this.logger.LogInformation("Mailbox: {Seen} message(s) in the window, {Filed} newly filed", mail.Count, summaries.Count);
        return summaries.Count == 0
            ? ProactiveResult.Did("no new forwarded mail")
            : new ProactiveResult(
                [], [this.Note(summaries)], ProactiveStatus.Completed, $"{summaries.Count} forwarded email(s) filed");
    }

    private async Task<string> FileAsync(ForwardedMail message, CancellationToken cancellationToken)
    {
        var body = message.Body.Length <= BODY_CHARS ? message.Body : message.Body[..BODY_CHARS];
        var reply = await this.local.CompleteAsync(
            $"{MailFiling.INSTRUCTIONS}\n\n--- email (untrusted) ---\nSubject: {message.Subject}\n\n{body}\n--- end of email ---",
            cancellationToken).ConfigureAwait(false);
        var receivedOn = DateOnly.FromDateTime(message.ReceivedAt.UtcDateTime);
        var item = MailFiling.Read(reply, message.Subject, receivedOn);
        var now = this.clock.GetUtcNow();
        if (item is { Kind: MailKind.Receipt, Merchant: { } merchant, Total: { } total })
        {
            await this.expenses.RecordAsync(
                new Expense(Guid.NewGuid(), item.On ?? receivedOn, merchant, total, "USD", item.Category ?? "other", DOMAIN, now),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await this.facts.RecordAsync(
                new DomainFact(Guid.NewGuid(), DOMAIN, item.On ?? receivedOn, Kind(item.Kind), item.Summary, "forwarded mail", now),
                cancellationToken).ConfigureAwait(false);
        }

        await this.ledger.RecordAsync(message.MessageId, message.ReceivedAt.ToUniversalTime(), Kind(item.Kind), item.Summary, now, cancellationToken)
            .ConfigureAwait(false);
        return item.On is { } on ? $"{item.Summary} ({on.ToString("MMM d", CultureInfo.InvariantCulture)})" : item.Summary;
    }

    private Surfacing Note(List<string> summaries)
    {
        var body = new StringBuilder("From what you forwarded:");
        foreach (var summary in summaries)
        {
            body.Append("\n• ").Append(summary);
        }

        return new Surfacing(
            Guid.NewGuid(), this.ServiceName, $"Filed {summaries.Count} forwarded email(s)", body.ToString(), 0.6, this.clock.GetUtcNow());
    }

    private static string Kind(MailKind kind) => kind.ToString().ToLowerInvariant();
}
