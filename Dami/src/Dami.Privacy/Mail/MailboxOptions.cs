namespace Dami.Privacy.Mail;

/// <summary>Dami's own mailbox. The password is an app password, a credential in proactive.env.</summary>
public sealed class MailboxOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "Mailbox";

    /// <summary>IMAP host; must be on the egress allowlist.</summary>
    public string Host { get; set; } = "imap.gmail.com";

    /// <summary>IMAP over TLS.</summary>
    public int Port { get; set; } = 993;

    /// <summary>The mailbox's address, e.g. dami.smhoff256@gmail.com.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary><c>Mailbox__Password</c>: an app password, never the account password, never in the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The folder read.</summary>
    public string Folder { get; set; } = "INBOX";
}
