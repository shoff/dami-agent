using Dami.Proactive.Mail;
using Xunit;

namespace Dami.Proactive.Tests.Mail;

/// <summary>The local model's reading of one forwarded email, turned into one filed item.</summary>
public sealed class MailFilingTests
{
    private static readonly DateOnly received = new(2026, 9, 29);

    [Fact]
    public void A_Receipt_Should_Carry_Its_Merchant_Total_And_Category()
    {
        var item = MailFiling.Read(
            """{"kind":"receipt","summary":"Amazon order: HDMI cable","date":"2026-09-28","merchant":"Amazon","total":"$12.99","category":"household"}""",
            "Your order", received);

        Assert.Equal(MailKind.Receipt, item.Kind);
        Assert.Equal(("Amazon", 12.99m, "household"), (item.Merchant, item.Total, item.Category));
        Assert.Equal(new DateOnly(2026, 9, 28), item.On);
    }

    [Fact]
    public void A_Receipt_Without_A_Total_Should_Be_Filed_As_Other()
    {
        var item = MailFiling.Read("""{"kind":"receipt","summary":"A shipping notice","merchant":"Amazon"}""", "Shipped", received);

        Assert.Equal(MailKind.Other, item.Kind);
    }

    [Fact]
    public void A_Dated_Item_Should_Keep_Its_Date_And_Summary()
    {
        var item = MailFiling.Read(
            """{"kind":"appointment","summary":"Vet visit for Milo at 10:00","date":"2026-10-03"}""", "Reminder", received);

        Assert.Equal((MailKind.Appointment, "Vet visit for Milo at 10:00", new DateOnly(2026, 10, 3)), (item.Kind, item.Summary, item.On));
    }

    [Theory]
    [InlineData("I can't help with that.")]
    [InlineData("""{"kind":"exfiltrate","summary":""}""")]
    public void An_Unreadable_Reply_Should_Fall_Back_To_The_Subject_As_Other(string reply)
    {
        var item = MailFiling.Read(reply, "Quarterly newsletter", received);

        Assert.Equal((MailKind.Other, "Quarterly newsletter"), (item.Kind, item.Summary));
    }

    [Fact]
    public void A_Summary_Should_Be_Bounded()
    {
        var item = MailFiling.Read($$"""{"kind":"other","summary":"{{new string('x', 900)}}"}""", "s", received);

        Assert.True(item.Summary.Length <= 241);
    }
}
