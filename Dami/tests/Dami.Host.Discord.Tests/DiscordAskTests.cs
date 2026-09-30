using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Life;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>"ask …": Steve's own records, answered locally with their sources, in his DM.</summary>
public sealed class DiscordAskTests
{
    private static readonly DateTimeOffset now = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    private readonly ILifeSource records = Substitute.For<ILifeSource>();
    private readonly IChatClient local = Substitute.For<IChatClient>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();

    private DiscordAsk Subject() => new(
        new LifeAnswerer([this.records], this.local, new FakeTimeProvider(now)), this.channel, NullLogger<DiscordAsk>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, now);

    [Fact]
    public async Task Ask_Should_Answer_With_Numbered_Sources_In_The_DM()
    {
        this.records.FindAsync("when did I last buy furnace filters?", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new LifeRecord(now.AddDays(-21), "receipt", "Menards $42.10 (household)")]);
        this.local.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("On Sep 12 at Menards [1].");

        Assert.True(await this.Subject().TryAnswerAsync(From("ask when did I last buy furnace filters?"), CancellationToken.None));

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.StartsWith("On Sep 12 at Menards [1].", StringComparison.Ordinal)
                && content.Text.Contains("[1] 2026-09-12 receipt: Menards $42.10 (household)", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_Found_Should_Say_So_Without_A_Model()
    {
        this.records.FindAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<LifeRecord>());

        await this.Subject().TryAnswerAsync(From("Ask where are my spare keys"), CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("nothing in your records", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await this.local.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Theory]
    [InlineData("ask")]
    [InlineData("can I ask you something")]
    [InlineData("asking for a friend")]
    public async Task Anything_Else_Should_Go_Elsewhere(string text)
    {
        Assert.False(await this.Subject().TryAnswerAsync(From(text), CancellationToken.None));
    }
}
