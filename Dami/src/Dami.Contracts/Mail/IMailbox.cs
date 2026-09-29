namespace Dami.Contracts.Mail;

/// <summary>One email in Dami's own mailbox, as read. The body is untrusted input.</summary>
public sealed record ForwardedMail(string MessageId, DateTimeOffset ReceivedAt, string From, string Subject, string Body);

/// <summary>
/// Dami's own mailbox, read-only: Steve forwards receipts, confirmations and appointments to
/// it. An egress seam — reading it is a connection off this host — so it is pinned with the others.
/// </summary>
public interface IMailbox
{
    /// <summary>Whether a mailbox is configured at all.</summary>
    bool IsConfigured { get; }

    /// <summary>Messages received in the last <paramref name="window"/>, oldest first, bodies bounded.</summary>
    Task<IReadOnlyList<ForwardedMail>> RecentAsync(TimeSpan window, CancellationToken cancellationToken);
}

/// <summary>Which forwarded emails have been filed, and as what.</summary>
public interface IMailLedger
{
    /// <summary>The Message-IDs among <paramref name="messageIds"/> already filed.</summary>
    Task<IReadOnlySet<string>> FiledAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);

    /// <summary>Records one filed email; false when it was already filed.</summary>
    Task<bool> RecordAsync(
        string messageId, DateTimeOffset receivedAt, string kind, string summary, DateTimeOffset filedAt,
        CancellationToken cancellationToken);
}
