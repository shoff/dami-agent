using Dami.Contracts.Finance;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Finance;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>
/// A receipt photo becomes a ledger row on this host: the local vision model reads it, the
/// ledger keeps it, and only Steve's own DM hears about it. No frontier model is involved.
/// </summary>
public sealed class DiscordReceiptResponderTests
{
    private static readonly DateTimeOffset received = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

    private readonly IVisionClient vision = Substitute.For<IVisionClient>();
    private readonly IDiscordRest rest = Substitute.For<IDiscordRest>();
    private readonly IExpenseLedger ledger = Substitute.For<IExpenseLedger>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();

    public DiscordReceiptResponderTests()
    {
        this.rest.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1 });
        this.ledger.RecordAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>()).Returns(true);
        this.ledger.BetweenAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Spent("Costco", 84.12m, 28), Spent("Aldi", 31.07m, 3), Spent("Shell", 40m, 10, "fuel")]);
    }

    private static Expense Spent(string merchant, decimal total, int day, string category = "groceries") =>
        new(Guid.NewGuid(), new DateOnly(2026, 9, day), merchant, total, "USD", category, "receipt-photo", received);

    private static InboundMessage Photo(string text) =>
        new("owner", "dm-7", text, received)
        {
            Attachments = [new InboundAttachment("IMG_1.jpg", "https://cdn/i.jpg", "image/jpeg", 900_000)],
        };

    private DiscordReceiptResponder Subject() => new(
        this.vision, this.rest, this.ledger, this.channel,
        new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true },
        new FakeTimeProvider(received), NullLogger<DiscordReceiptResponder>.Instance);

    private void VisionSays(string reply) =>
        this.vision.DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), ReceiptReader.PROMPT, Arg.Any<CancellationToken>()).Returns(reply);

    [Fact]
    public async Task A_Receipt_Photo_Should_Be_Read_Locally_Recorded_And_Summed_For_The_Month()
    {
        this.VisionSays("""{"merchant":"Costco","date":"2026-09-28","total":84.12,"category":"groceries"}""");

        Assert.True(await this.Subject().TryAnswerAsync(Photo("receipt"), CancellationToken.None));

        await this.ledger.Received(1).RecordAsync(
            Arg.Is<Expense>(expense => expense.Merchant == "Costco" && expense.Total == 84.12m),
            Arg.Any<CancellationToken>());
        await this.ledger.Received(1).BetweenAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 29), Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.ConversationId == "dm-7"
                && content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("$84.12 at Costco", StringComparison.Ordinal)
                && content.Text.Contains("Groceries this month: $115.19", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("what is this")]
    [InlineData("")]
    public async Task A_Photo_That_Is_Not_Called_A_Receipt_Should_Go_Elsewhere(string text)
    {
        Assert.False(await this.Subject().TryAnswerAsync(Photo(text), CancellationToken.None));

        await this.vision.DidNotReceiveWithAnyArgs().DescribeAsync(default, default!, default);
    }

    [Fact]
    public async Task The_Word_Without_A_Photo_Should_Go_Elsewhere()
    {
        Assert.False(await this.Subject().TryAnswerAsync(
            new InboundMessage("owner", "dm-7", "where did I put the receipt", received), CancellationToken.None));
    }

    [Fact]
    public async Task An_Unreadable_Receipt_Should_Be_Said_And_Not_Recorded()
    {
        this.VisionSays("{}");

        Assert.True(await this.Subject().TryAnswerAsync(Photo("Receipt from lunch"), CancellationToken.None));

        await this.ledger.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("couldn't read", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Receipt_Already_Logged_Should_Say_So()
    {
        this.VisionSays("""{"merchant":"Costco","date":"2026-09-28","total":84.12,"category":"groceries"}""");
        this.ledger.RecordAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>()).Returns(false);

        await this.Subject().TryAnswerAsync(Photo("receipt"), CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.StartsWith("Already logged", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
}
