using Dami.Contracts.Privacy;
using Dami.Core.Reliability;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>
/// A smoke detector, not a muse (ADR-0014 §2): a sidecar that fails real work twice in a row
/// is said once, and its recovery once. On 2026-09-27 the GPU sidecars were stopped under a
/// running dami-host and nobody heard for forty hours.
/// </summary>
public sealed class DiscordOutageAlarmTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly ISidecarProbe embeddings = Substitute.For<ISidecarProbe>();
    private readonly DiscordOptions options = new()
    {
        Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7",
    };

    public DiscordOutageAlarmTests()
    {
        this.embeddings.Name.Returns("embeddings");
    }

    private DiscordOutageAlarm Subject() =>
        new([this.embeddings], this.channel, this.options, new FakeTimeProvider(now), NullLogger<DiscordOutageAlarm>.Instance);

    private void Down() =>
        this.embeddings.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException("Connection refused (127.0.0.1:8080)")));

    private void Up() => this.embeddings.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

    private int Sent() => this.channel.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IEgressChannel.SendAsync));

    [Fact]
    public async Task One_Failure_Should_Say_Nothing_Two_In_A_Row_Should_Say_It_Once()
    {
        this.Down();
        var alarm = this.Subject();

        await alarm.TickAsync(CancellationToken.None);
        Assert.Equal(0, this.Sent());
        await alarm.TickAsync(CancellationToken.None);
        await alarm.TickAsync(CancellationToken.None);

        Assert.Equal(1, this.Sent());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.ConversationId == "dm-7"
                && content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("embeddings", StringComparison.Ordinal)
                && content.Text.Contains("Connection refused", StringComparison.Ordinal)
                && content.Text.Contains("Resume Dami", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recovery_After_An_Alarm_Should_Be_Said_Once()
    {
        this.Down();
        var alarm = this.Subject();
        await alarm.TickAsync(CancellationToken.None);
        await alarm.TickAsync(CancellationToken.None);

        this.Up();
        await alarm.TickAsync(CancellationToken.None);
        await alarm.TickAsync(CancellationToken.None);

        Assert.Equal(2, this.Sent());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.StartsWith("✅ embeddings is back", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Blip_That_Recovers_Before_The_Second_Look_Should_Say_Nothing()
    {
        this.Down();
        var alarm = this.Subject();
        await alarm.TickAsync(CancellationToken.None);
        this.Up();
        await alarm.TickAsync(CancellationToken.None);

        Assert.Equal(0, this.Sent());
    }

    [Fact]
    public async Task Without_A_Conversation_Should_Not_Probe()
    {
        this.options.CheckInConversationId = string.Empty;

        await this.Subject().TickAsync(CancellationToken.None);

        await this.embeddings.DidNotReceiveWithAnyArgs().ProbeAsync(default);
    }
}
