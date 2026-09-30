using Dami.Contracts.Privacy;
using Dami.Contracts.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>"pause", "pause 3h", "resume": the switch for everything Dami starts on her own (A10).</summary>
public sealed class DiscordPauseCommandTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 21, 0, 0, TimeSpan.Zero);

    private readonly IPauseSwitch pause = Substitute.For<IPauseSwitch>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();

    private DiscordPauseCommand Subject() => new(this.pause, this.channel, new FakeTimeProvider(now), NullLogger<DiscordPauseCommand>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, now);

    [Theory]
    [InlineData("pause", null)]
    [InlineData("Pause 3h", 3.0)]
    [InlineData("!pause 2d", 48.0)]
    [InlineData("pause 90m", 1.5)]
    public async Task Pause_Should_Set_The_Switch_And_Say_How_Long(string text, double? hours)
    {
        Assert.True(await this.Subject().TryAnswerAsync(From(text), CancellationToken.None));

        await this.pause.Received(1).PauseAsync(
            hours is null ? null : now.AddHours(hours.Value), "paused from Discord", now, Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("Paused", StringComparison.Ordinal)
                && content.Text.Contains("resume", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resume_Should_Lift_The_Switch()
    {
        Assert.True(await this.Subject().TryAnswerAsync(From("resume"), CancellationToken.None));

        await this.pause.Received(1).ResumeAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("should I pause the build")]
    [InlineData("pause for a moment and think")]
    [InlineData("pause 3x")]
    public async Task Anything_Else_Should_Go_Elsewhere(string text)
    {
        Assert.False(await this.Subject().TryAnswerAsync(From(text), CancellationToken.None));

        await this.pause.DidNotReceiveWithAnyArgs().PauseAsync(default, default!, default, default);
    }
}
