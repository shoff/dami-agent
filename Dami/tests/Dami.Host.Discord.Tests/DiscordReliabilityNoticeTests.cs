using Dami.Contracts.Privacy;
using Dami.Core.Reliability;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>Silent failures stop being silent: named in the DM once a day, summed up on Sundays.</summary>
public sealed class DiscordReliabilityNoticeTests
{
    // 2026-09-29 is a Tuesday in CDT (UTC-5): 09:00 Chicago is 14:00 UTC. 2026-10-04 is a Sunday.
    private static readonly DateTimeOffset tuesdayAfterNine = new(2026, 9, 29, 14, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset tuesdayBeforeNine = new(2026, 9, 29, 13, 55, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset sundayAfterNine = new(2026, 10, 4, 14, 5, 0, TimeSpan.Zero);

    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly IReliabilityReport report = Substitute.For<IReliabilityReport>();
    private readonly DiscordOptions options = new()
    {
        Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7",
    };

    private readonly string state = Path.Combine(Path.GetTempPath(), "dami-notice-" + Guid.NewGuid().ToString("N"));

    private FakeTimeProvider clock = new(tuesdayAfterNine);

    private DiscordReliabilityNotice Subject() =>
        new(this.report, this.channel, this.options, this.clock, this.state, NullLogger<DiscordReliabilityNotice>.Instance);

    private void Reads(params string[] problems) =>
        this.report.ReadAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ReliabilityReading(problems, 170, 2));

    [Fact]
    public async Task Should_Name_Yesterdays_Problems_In_The_DM()
    {
        this.Reads("embedder (nightly) has not run for 28 hours", "gallery-curator failed 1 of 3 passes");

        Assert.True(await this.Subject().TickAsync(CancellationToken.None));

        await this.report.Received(1).ReadAsync(tuesdayAfterNine.AddDays(-1), Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.ConversationId == "dm-7"
                && content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("embedder (nightly) has not run for 28 hours", StringComparison.Ordinal)
                && content.Text.Contains("gallery-curator failed 1 of 3 passes", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Clean_Weekday_Should_Send_Nothing()
    {
        this.Reads();

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.channel.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Sunday_Should_Sum_Up_The_Week_Even_When_It_Was_Clean()
    {
        this.clock = new FakeTimeProvider(sundayAfterNine);
        this.Reads();

        Assert.True(await this.Subject().TickAsync(CancellationToken.None));

        await this.report.Received(1).ReadAsync(sundayAfterNine.AddDays(-7), Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("170", StringComparison.Ordinal)
                && content.Text.Contains("nothing else went wrong", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Wait_For_The_Hour_And_Speak_Once_A_Day()
    {
        this.clock = new FakeTimeProvider(tuesdayBeforeNine);
        this.Reads("something broke");
        var subject = this.Subject();

        Assert.False(await subject.TickAsync(CancellationToken.None));
        this.clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await subject.TickAsync(CancellationToken.None));
        this.clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(await subject.TickAsync(CancellationToken.None));

        await this.channel.Received(1).SendAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Restart_Should_Not_Say_The_Same_Day_Again()
    {
        // Every deploy restarts the host; the in-memory "said it today" would reset each time.
        this.Reads("something broke");
        Assert.True(await this.Subject().TickAsync(CancellationToken.None));

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.channel.Received(1).SendAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Unwritable_Memory_Should_Not_Stop_The_Notice()
    {
        // 2026-09-29: ~/.local/share/dami is root-owned; UnauthorizedAccessException escaped
        // the tick and the notice was never sent at all.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var locked = Directory.CreateTempSubdirectory("dami-locked-");
        File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            this.Reads("something broke");
            var subject = new DiscordReliabilityNotice(
                this.report, this.channel, this.options, this.clock,
                Path.Combine(locked.FullName, "sub", "reliability-notice"), NullLogger<DiscordReliabilityNotice>.Instance);

            Assert.True(await subject.TickAsync(CancellationToken.None));
        }
        finally
        {
            File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            locked.Delete(true);
        }
    }

    [Fact]
    public async Task Should_Do_Nothing_Without_A_Configured_Conversation()
    {
        this.options.CheckInConversationId = string.Empty;
        this.Reads("something broke");

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.report.DidNotReceiveWithAnyArgs().ReadAsync(default, default);
    }
}
