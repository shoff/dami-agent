using Dami.Core.Finance;
using Xunit;

namespace Dami.Core.Tests.Finance;

/// <summary>The local vision model's reading of a receipt, turned into one expense or nothing.</summary>
public sealed class ReceiptReaderTests
{
    private static readonly DateOnly sent = new(2026, 9, 29);
    private static readonly DateTimeOffset now = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Read_A_Well_Formed_Reply()
    {
        var expense = ReceiptReader.Read(
            """Here you go: {"merchant":"Costco Wholesale","date":"2026-09-28","total":84.12,"currency":"usd","category":"Groceries"}""",
            sent, now);

        Assert.NotNull(expense);
        Assert.Equal("Costco Wholesale", expense!.Merchant);
        Assert.Equal(new DateOnly(2026, 9, 28), expense.SpentOn);
        Assert.Equal(84.12m, expense.Total);
        Assert.Equal("USD", expense.Currency);
        Assert.Equal("groceries", expense.Category);
        Assert.Equal("receipt-photo", expense.Source);
    }

    [Fact]
    public void Should_Accept_A_Total_Written_As_Money()
    {
        var expense = ReceiptReader.Read("""{"merchant":"Aldi","date":null,"total":"$1,031.07","category":"groceries"}""", sent, now);

        Assert.Equal(1031.07m, expense!.Total);
        Assert.Equal(sent, expense.SpentOn);
        Assert.Equal("USD", expense.Currency);
    }

    [Theory]
    [InlineData("2027-01-01")]
    [InlineData("2024-03-02")]
    [InlineData("not a date")]
    public void An_Implausible_Date_Should_Fall_Back_To_The_Day_It_Was_Sent(string date)
    {
        var expense = ReceiptReader.Read($$"""{"merchant":"Aldi","date":"{{date}}","total":5,"category":"groceries"}""", sent, now);

        Assert.Equal(sent, expense!.SpentOn);
    }

    [Fact]
    public void An_Unknown_Category_Should_Be_Other()
    {
        var expense = ReceiptReader.Read("""{"merchant":"Hobby Lobby","total":12.5,"category":"crafts"}""", sent, now);

        Assert.Equal("other", expense!.Category);
    }

    [Theory]
    [InlineData("I can't read this receipt.")]
    [InlineData("""{"merchant":"Aldi","total":0}""")]
    [InlineData("""{"merchant":"","total":12}""")]
    [InlineData("""{"merchant":"Aldi"}""")]
    [InlineData("""{"merchant":"Aldi","total":"lots"}""")]
    public void A_Reply_Without_A_Merchant_And_A_Total_Should_Be_Nothing(string reply)
    {
        Assert.Null(ReceiptReader.Read(reply, sent, now));
    }
}
