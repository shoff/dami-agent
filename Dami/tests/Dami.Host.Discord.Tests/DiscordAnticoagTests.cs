using Dami.Contracts.Anticoag;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>ADR-0037 slice 1: readings and doses logged on this host; interactors noticed; never dosing.</summary>
public sealed class DiscordAnticoagTests
{
    // 2026-09-29 16:00 CDT.
    private static readonly DateTimeOffset now = new(2026, 9, 29, 21, 0, 0, TimeSpan.Zero);

    private readonly IAnticoagLog log = Substitute.For<IAnticoagLog>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly AnticoagOptions options = new() { TargetLow = 2.0m, TargetHigh = 3.0m, TimeZone = "America/Chicago" };

    public DiscordAnticoagTests()
    {
        this.log.ReadingsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 29), 2.4m, now),
            new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 22), 2.1m, now),
            new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 15), 2.6m, now),
        ]);
    }

    private DiscordAnticoag Subject() =>
        new(this.log, this.channel, Options.Create(this.options), new FakeTimeProvider(now), NullLogger<DiscordAnticoag>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, now);

    private Task<string> SaidAsync() =>
        Task.FromResult(((OutboundContent)this.channel.ReceivedCalls().Single().GetArguments()[0]!).Text);

    [Fact]
    public async Task A_Reading_In_Range_Should_Be_Logged_With_Its_Trend_To_The_DM()
    {
        Assert.True(await this.Subject().TryAnswerAsync(From("INR 2.4"), CancellationToken.None));

        await this.log.Received(1).RecordAsync(
            Arg.Is<InrReading>(reading => reading.Inr == 2.4m && reading.OnDay == new DateOnly(2026, 9, 29)), Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.ProfileDerived), Arg.Any<CancellationToken>());
        var said = await this.SaidAsync();
        Assert.Contains("within your 2.0–3.0 range", said, StringComparison.Ordinal);
        Assert.Contains("2.1 (Sep 22), 2.6 (Sep 15)", said, StringComparison.Ordinal);
        Assert.Contains("7 days since the reading before", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_High_Reading_Should_Point_To_The_Clinic_And_Name_The_Urgent_Signs_And_Never_A_Dose()
    {
        await this.Subject().TryAnswerAsync(From("INR was 3.6 today"), CancellationToken.None);

        var said = await this.SaidAsync();
        Assert.Contains("above your 2.0–3.0 range", said, StringComparison.Ordinal);
        Assert.Contains("clinic decides any change", said, StringComparison.Ordinal);
        Assert.Contains("black or bloody stools", said, StringComparison.Ordinal);
        Assert.DoesNotContain("mg", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_A_Configured_Range_There_Should_Be_No_Range_Line()
    {
        this.options.TargetLow = null;

        await this.Subject().TryAnswerAsync(From("INR 2.4"), CancellationToken.None);

        Assert.DoesNotContain("range", await this.SaidAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Dose_Change_Should_Be_Kept_Verbatim_And_Never_Commented_On()
    {
        Assert.True(await this.Subject().TryAnswerAsync(From("clinic changed me to 7.5 mg Mon/Wed, 5 mg other days"), CancellationToken.None));

        await this.log.Received(1).RecordAsync(
            Arg.Is<DoseChange>(dose => dose.Dose == "clinic changed me to 7.5 mg Mon/Wed, 5 mg other days"), Arg.Any<CancellationToken>());
        Assert.Contains("never suggest doses", await this.SaidAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anything_Else_Should_Go_Elsewhere()
    {
        Assert.False(await this.Subject().TryAnswerAsync(From("what's a normal INR?"), CancellationToken.None));
    }
}

public sealed class DiscordInteractionWatchTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 21, 0, 0, TimeSpan.Zero);

    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly FakeTimeProvider clock = new(now);

    private DiscordInteractionWatch Subject() => new(this.channel, this.clock, NullLogger<DiscordInteractionWatch>.Instance);

    private static InboundMessage From(string text) => new("owner", "dm-7", text, now);

    [Fact]
    public async Task An_Interactor_Should_Be_Noticed_Once_A_Week_As_A_Prompt_To_Ask()
    {
        var watch = this.Subject();

        await watch.NoticeAsync(From("doc put me on Bactrim for a UTI"), CancellationToken.None);
        await watch.NoticeAsync(From("second day of bactrim"), CancellationToken.None);
        this.clock.Advance(TimeSpan.FromDays(8));
        await watch.NoticeAsync(From("bactrim again"), CancellationToken.None);

        await this.channel.Received(2).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("Bactrim can raise INR", StringComparison.Ordinal)
                && content.Text.Contains("asking whoever prescribed it", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ordinary_Talk_Should_Send_Nothing()
    {
        await this.Subject().NoticeAsync(From("went for a run"), CancellationToken.None);

        await this.channel.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
