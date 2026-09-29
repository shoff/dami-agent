using Dami.Contracts.Mail;
using Dami.Contracts.Privacy;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Privacy.Mail;

/// <summary>Reads Dami's own mailbox over IMAP, read-only (docs/agent-landscape-2026-09.md D2).</summary>
/// <remarks>
/// An egress seam that is not HTTP, so it enforces the same allowlist the HTTP door does
/// before it connects: a host that is not listed is refused, not tried. The folder is opened
/// read-only — nothing is marked, moved or deleted — and nothing is ever sent. Bodies are
/// bounded here, before anything else sees them.
/// </remarks>
public sealed class ImapMailbox : IMailbox
{
    private const int BODY_CHARS = 8000;
    private const int MESSAGES = 50;

    private readonly MailboxOptions mailboxOptions;
    private readonly EgressOptions egressOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ImapMailbox> logger;

    /// <summary>Creates the mailbox.</summary>
    public ImapMailbox(
        IOptions<MailboxOptions> mailboxOptions, IOptions<EgressOptions> egressOptions, TimeProvider clock, ILogger<ImapMailbox> logger)
    {
        ArgumentNullException.ThrowIfNull(mailboxOptions);
        ArgumentNullException.ThrowIfNull(egressOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.mailboxOptions = mailboxOptions.Value;
        this.egressOptions = egressOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => this.mailboxOptions.User.Length > 0 && this.mailboxOptions.Password.Length > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForwardedMail>> RecentAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        if (!this.egressOptions.AllowedHosts.Any(host => string.Equals(host, this.mailboxOptions.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new EgressRefusedException($"host '{this.mailboxOptions.Host}' is not on the egress allowlist");
        }

        using var client = new ImapClient();
        await client.ConnectAsync(this.mailboxOptions.Host, this.mailboxOptions.Port, SecureSocketOptions.SslOnConnect, cancellationToken)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(this.mailboxOptions.User, this.mailboxOptions.Password, cancellationToken).ConfigureAwait(false);
        var folder = await client.GetFolderAsync(this.mailboxOptions.Folder, cancellationToken).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
        var since = this.clock.GetUtcNow().UtcDateTime.Date - window;
        var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since), cancellationToken).ConfigureAwait(false);
        var mail = new List<ForwardedMail>();
        foreach (var uid in uids.TakeLast(MESSAGES))
        {
            var message = await folder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
            var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
            mail.Add(new ForwardedMail(
                message.MessageId ?? $"uid-{uid.Id}@{this.mailboxOptions.Folder}",
                message.Date,
                message.From.ToString(),
                message.Subject ?? string.Empty,
                body.Length <= BODY_CHARS ? body : body[..BODY_CHARS]));
        }

        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Mailbox read: {Count} message(s) since {Since:yyyy-MM-dd}", mail.Count, since);
        return [.. mail.OrderBy(item => item.ReceivedAt)];
    }
}
