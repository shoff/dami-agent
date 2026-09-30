using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Runtime;
using Dami.Contracts.Scheduling;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>"next": what Dami will do on her own, before she does it (H7).</summary>
public sealed class DiscordUpcomingTests
{
    // 16:00 CDT.
    private static readonly DateTimeOffset now = new(2026, 9, 29, 21, 0, 0, TimeSpan.Zero);

    private readonly IScheduledJobStore jobs = Substitute.For<IScheduledJobStore>();
    private readonly IProactiveRunHistory history = Substitute.For<IProactiveRunHistory>();
    private readonly IPauseSwitch pause = Substitute.For<IPauseSwitch>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();

    public DiscordUpcomingTests()
    {
        this.jobs.ListAsync(Arg.Any<CancellationToken>()).Returns([
            new ScheduledJob(Guid.NewGuid(), "portrait every six hours", "d", ScheduledJobKind.Prompt, "p", [], "0 */6 * * *", "UTC",
                ScheduledJobStatus.Active, now, now, now.AddHours(2), null, null, "discord:1"),
            new ScheduledJob(Guid.NewGuid(), "draft only", "d", ScheduledJobKind.Prompt, "p", [], "0 * * * *", "UTC",
                ScheduledJobStatus.Draft, now, null, null, null, null)]);
        this.history.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([
            new ProactiveServiceHistory("daily-diary", 3, now.AddHours(-20), ProactiveStatus.Completed, ProactiveCadence.Nightly, 0, 0, 0, []),
            new ProactiveServiceHistory("reflection", 3, now.AddDays(-2), ProactiveStatus.Completed, ProactiveCadence.Weekly, 0, 0, 0, [])]);
    }

    private DiscordUpcoming Subject() => new(
        this.jobs, this.history, this.pause, this.channel,
        new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7" },
        new FakeTimeProvider(now), NullLogger<DiscordUpcoming>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, now);

    [Fact]
    public async Task Next_Should_List_Jobs_And_Services_In_The_Order_They_Will_Happen()
    {
        Assert.True(await this.Subject().TryAnswerAsync(From("next"), CancellationToken.None));

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("job \"portrait every six hours\" — 6:00 PM", StringComparison.Ordinal)
                && content.Text.Contains("daily-diary — ", StringComparison.Ordinal)
                && !content.Text.Contains("draft only", StringComparison.Ordinal)
                && content.Text.IndexOf("portrait", StringComparison.Ordinal) < content.Text.IndexOf("daily-diary", StringComparison.Ordinal)
                && content.Text.IndexOf("daily-diary", StringComparison.Ordinal) < content.Text.IndexOf("reflection", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task While_Paused_It_Should_Say_So_First()
    {
        this.pause.CurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PauseState(null, "paused from Discord", now));

        await this.Subject().TryAnswerAsync(From("upcoming"), CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.StartsWith("⏸️ Paused", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anything_Else_Should_Go_Elsewhere()
    {
        Assert.False(await this.Subject().TryAnswerAsync(From("what's next for the build"), CancellationToken.None));
    }
}
