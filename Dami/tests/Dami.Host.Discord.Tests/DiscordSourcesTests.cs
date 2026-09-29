using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>"Why did you say that?" — every local line the last answer put before the gate, and what became of it.</summary>
public sealed class DiscordSourcesTests
{
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly TurnDisclosures disclosures = new();
    private readonly DiscordLastTurns lastTurns = new();

    private DiscordSources Subject() => new(this.lastTurns, this.disclosures, this.channel, NullLogger<DiscordSources>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("sources")]
    [InlineData("Why?")]
    [InlineData("!sources")]
    public async Task Should_List_What_The_Last_Answer_Drew_On_To_Steves_DM(string text)
    {
        var trace = Guid.NewGuid();
        this.lastTurns.Answered("dm-7", trace);
        this.disclosures.Remember(trace,
        [
            new DisclosedItem("Standing lesson from Steve (follow it): be brief", Disclosure.Pass, "…", "instruction"),
            new DisclosedItem("Steve's INR was 2.4", Disclosure.Disguise, "Someone's INR was 2.4", "identity not needed"),
            new DisclosedItem("Riza was diagnosed with BPD", Disclosure.Withhold, string.Empty, "another person's health"),
        ]);

        Assert.True(await this.Subject().TryAnswerAsync(From(text), CancellationToken.None));

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.ConversationId == "dm-7"
                && content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("3 local line(s)", StringComparison.Ordinal)
                && content.Text.Contains("✅ sent: Standing lesson from Steve (follow it): be brief", StringComparison.Ordinal)
                && content.Text.Contains("🎭 disguised as \"Someone's INR was 2.4\": Steve's INR was 2.4", StringComparison.Ordinal)
                && content.Text.Contains("⛔ withheld (another person's health): Riza was diagnosed with BPD", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_So_When_There_Is_No_Recent_Answer_To_Explain()
    {
        Assert.True(await this.Subject().TryAnswerAsync(From("sources"), CancellationToken.None));

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("no recent answer", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("why is the sky blue")]
    [InlineData("what are your sources on that")]
    public async Task Anything_Else_Should_Go_Elsewhere(string text)
    {
        Assert.False(await this.Subject().TryAnswerAsync(From(text), CancellationToken.None));
    }

    [Fact]
    public async Task A_Long_List_Should_Stay_Within_One_Discord_Message()
    {
        var trace = Guid.NewGuid();
        this.lastTurns.Answered("dm-7", trace);
        this.disclosures.Remember(trace,
            [.. Enumerable.Range(0, 60).Select(index => new DisclosedItem(new string('x', 300) + index, Disclosure.Pass, "s", "ok"))]);

        await this.Subject().TryAnswerAsync(From("sources"), CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Length <= 2000), Arg.Any<CancellationToken>());
    }
}
