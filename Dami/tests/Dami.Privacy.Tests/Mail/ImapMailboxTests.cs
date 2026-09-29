using Dami.Contracts.Privacy;
using Dami.Privacy.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Privacy.Tests.Mail;

/// <summary>Dami's mailbox is an egress seam like the others: no allowlisted host, no connection.</summary>
public sealed class ImapMailboxTests
{
    private static ImapMailbox Subject(MailboxOptions mailbox, params string[] allowed)
    {
        var egress = new EgressOptions();
        foreach (var host in allowed)
        {
            egress.AllowedHosts.Add(host);
        }

        return new ImapMailbox(Options.Create(mailbox), Options.Create(egress), TimeProvider.System, NullLogger<ImapMailbox>.Instance);
    }

    [Fact]
    public void A_Mailbox_Without_Credentials_Should_Not_Be_Configured()
    {
        Assert.False(Subject(new MailboxOptions { User = "dami@example.com" }).IsConfigured);
        Assert.True(Subject(new MailboxOptions { User = "dami@example.com", Password = "app-password" }).IsConfigured);
    }

    [Fact]
    public async Task A_Host_Off_The_Allowlist_Should_Be_Refused_Before_Any_Connection()
    {
        var mailbox = Subject(new MailboxOptions { User = "dami@example.com", Password = "p", Host = "imap.gmail.com" }, "api.weather.gov");

        var refused = await Assert.ThrowsAsync<EgressRefusedException>(
            () => mailbox.RecentAsync(TimeSpan.FromDays(7), CancellationToken.None));

        Assert.Contains("imap.gmail.com", refused.Message, StringComparison.Ordinal);
    }
}
