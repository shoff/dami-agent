using Dami.Contracts.Anticoag;
using Dami.Contracts.Nutrition;
using Dami.Contracts.Privacy;
using Dami.Contracts.Runtime;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>The Monday anticoagulation note (ADR-0037 slice 2): DM only, once a week, never while paused.</summary>
public sealed class DiscordAnticoagWeeklyTests : IDisposable
{
    // Monday 2026-10-26 09:05 CDT; Tuesday is the day after.
    private static readonly DateTimeOffset monday = new(2026, 10, 26, 14, 5, 0, TimeSpan.Zero);

    private readonly IAnticoagLog log = Substitute.For<IAnticoagLog>();
    private readonly IMealLog meals = Substitute.For<IMealLog>();
    private readonly IPauseSwitch pause = Substitute.For<IPauseSwitch>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly string state = Path.Combine(Path.GetTempPath(), "dami-anticoag-" + Guid.NewGuid().ToString("N"));

    public DiscordAnticoagWeeklyTests()
    {
        this.log.ReadingsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            [new InrReading(Guid.NewGuid(), new DateOnly(2026, 10, 19), 2.4m, monday)]);
        this.log.DosesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DoseChange>());
        this.meals.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Meal>());
    }

    public void Dispose() => File.Delete(this.state);

    private DiscordAnticoagWeekly Subject(DateTimeOffset now) => new(
        this.log, this.meals, this.pause, this.channel,
        new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7" },
        new FakeTimeProvider(now), this.state, NullLogger<DiscordAnticoagWeekly>.Instance);

    [Fact]
    public async Task Monday_After_The_Hour_Should_Send_The_Note_Once_To_The_DM()
    {
        Assert.True(await this.Subject(monday).TickAsync(CancellationToken.None));
        Assert.False(await this.Subject(monday.AddHours(1)).TickAsync(CancellationToken.None));

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.ConversationId == "dm-7"
                && content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("Last INR 2.4 on Oct 19, 7 days ago", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Any_Other_Day_Should_Say_Nothing()
    {
        Assert.False(await this.Subject(monday.AddDays(1)).TickAsync(CancellationToken.None));
    }

    [Fact]
    public async Task While_Paused_Should_Say_Nothing()
    {
        this.pause.CurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PauseState(null, "away", monday));

        Assert.False(await this.Subject(monday).TickAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_Logged_Should_Say_Nothing()
    {
        this.log.ReadingsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InrReading>());

        Assert.False(await this.Subject(monday).TickAsync(CancellationToken.None));
        await this.channel.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
