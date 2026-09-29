using Dami.Contracts.Gallery;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Gallery;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>
/// ADR-0014 as amended 2026-09-29: once a day, the single best pending surfacing goes to
/// Steve's DM as a frontier turn, and is marked as pushed so H8 does not tune on it.
/// </summary>
public sealed class DiscordDailyCheckInTests
{
    // 2026-09-29 is CDT, UTC-5: 09:00 in Chicago is 14:00 UTC.
    private static readonly DateTimeOffset beforeNine = new(2026, 9, 29, 13, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset afterNine = new(2026, 9, 29, 14, 5, 0, TimeSpan.Zero);

    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly IProgressiveEgressChannel progressive = Substitute.For<IProgressiveEgressChannel>();
    private readonly IAugmentedTurn augmented = Substitute.For<IAugmentedTurn>();
    private readonly IConversationTurnStore turnStore = Substitute.For<IConversationTurnStore>();
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();
    private readonly ISurfacingQueue queue = Substitute.For<ISurfacingQueue>();
    private readonly ISpeechClient speech = Substitute.For<ISpeechClient>();
    private FakeTimeProvider clock = new(afterNine);
    private readonly DiscordOptions options = new()
    {
        Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7",
    };

    private static readonly Surfacing weak = new(Guid.NewGuid(), "scout", "weak find", "meh", 0.55, afterNine.AddDays(-2));
    private static readonly Surfacing strong = new(Guid.NewGuid(), "recall-sentinel", "strong find", "your dryer is recalled", 0.9, afterNine.AddDays(-1));

    public DiscordDailyCheckInTests()
    {
        this.turnStore.RecentCompletedTurnsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => NoneAsync<ConversationTurn>());
        this.queue.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => ManyAsync(weak, strong));
        this.queue.LastPushedAtAsync(DiscordDailyCheckIn.VIA, Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)afterNine.AddDays(-1));
        this.progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>()).Returns("m1");
        this.augmented.StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => new AugmentedTurnStream(Guid.NewGuid(), 1, 100, ManyAsync("Your dryer is on a recall list.")));
    }

    [Fact]
    public async Task Should_Send_The_Single_Strongest_Surfacing_To_The_Configured_DM()
    {
        var sent = await this.Subject().TickAsync(CancellationToken.None);

        Assert.True(sent);
        await this.augmented.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(lines =>
                lines.Any(line => line.Contains("strong find", StringComparison.Ordinal))
                && !lines.Any(line => line.Contains("weak find", StringComparison.Ordinal))),
            Arg.Any<FrontierToolbox>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await this.progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content => content.ConversationId == "dm-7"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Mark_It_Pushed_Rather_Than_Delivered_So_H8_Does_Not_Tune_On_It()
    {
        await this.Subject().TickAsync(CancellationToken.None);

        await this.queue.Received(1).PushedAsync(
            strong.SurfacingId, DiscordDailyCheckIn.VIA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await this.queue.DidNotReceive().DeliverAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await this.queue.DidNotReceive().PushedAsync(
            weak.SurfacingId, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Record_The_Check_In_As_Something_Steve_Said()
    {
        await this.Subject().TickAsync(CancellationToken.None);

        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Repo_Hygiene_Even_When_It_Is_The_Strongest()
    {
        // 2026-09-29: the first check-in was repo-hygiene's "2 things are adrift in the
        // working copy" at confidence 1.0, and Steve asked for it to be left out.
        var hygiene = new Surfacing(Guid.NewGuid(), "repo-hygiene", "adrift", "two files", 1.0, afterNine.AddDays(-1));
        this.queue.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(hygiene, weak));

        await this.Subject().TickAsync(CancellationToken.None);

        await this.queue.Received(1).PushedAsync(
            weak.SurfacingId, DiscordDailyCheckIn.VIA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await this.queue.DidNotReceive().PushedAsync(
            hygiene.SurfacingId, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Stay_Silent_When_Only_Excluded_Services_Are_Pending()
    {
        this.options.CheckInExcludedServices = ["scout", "recall-sentinel"];

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.progressive.DidNotReceive().BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Follow_The_Check_In_With_It_Read_Aloud()
    {
        // Audio briefs were among the most-kept habits in the 2026-09-29 survey. The words
        // already went to Discord; the voice is the same words, rendered on this host.
        this.speech.SpeakAsync("Your dryer is on a recall list.", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2 });

        await this.Subject().TickAsync(CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.ConversationId == "dm-7"
                && content.Attachments.Count == 1
                && content.Attachments[0].FileName == "check-in.wav"
                && content.Attachments[0].ContentType == "audio/wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Failed_Voice_Should_Not_Undo_A_Sent_Check_In()
    {
        this.speech.SpeakAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new HttpRequestException("tts down"));

        Assert.True(await this.Subject().TickAsync(CancellationToken.None));
        await this.queue.Received(1).PushedAsync(
            strong.SurfacingId, DiscordDailyCheckIn.VIA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Speak_When_The_Voice_Is_Off()
    {
        this.options.CheckInVoice = false;

        await this.Subject().TickAsync(CancellationToken.None);

        await this.speech.DidNotReceiveWithAnyArgs().SpeakAsync(default!, default);
    }

    [Fact]
    public async Task Should_Wait_For_The_Hour()
    {
        this.clock = new FakeTimeProvider(beforeNine);

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.progressive.DidNotReceive().BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_At_Most_One_A_Day_Across_Restarts()
    {
        this.queue.LastPushedAtAsync(DiscordDailyCheckIn.VIA, Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)afterNine.AddMinutes(-2));

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.progressive.DidNotReceive().BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Stay_Silent_On_A_Day_With_Nothing_Pending()
    {
        this.queue.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => NoneAsync<Surfacing>());

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        await this.augmented.DidNotReceive().StreamAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Try_Once_A_Day_When_The_Frontier_Fails_Rather_Than_Every_Poll()
    {
        // A failure is explained in the DM; retrying every five minutes would fill it with apologies.
        this.augmented.StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<AugmentedTurnStream>>(_ => throw new InvalidOperationException("codex down"));
        var subject = this.Subject();

        Assert.False(await subject.TickAsync(CancellationToken.None));
        this.clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await subject.TickAsync(CancellationToken.None));

        await this.augmented.Received(1).StreamAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await this.queue.DidNotReceive().PushedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Do_Nothing_Without_A_Configured_Conversation()
    {
        this.options.CheckInConversationId = string.Empty;

        Assert.False(await this.Subject().TickAsync(CancellationToken.None));

        _ = this.queue.DidNotReceive().PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private DiscordDailyCheckIn Subject()
    {
        var lessons = Substitute.For<IStandingLessons>();
        lessons.LinesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var answerer = new DiscordAnswerer(
            this.channel, this.augmented, new DiscordReplyStreamer(this.progressive), Bundle(),
            new DiscordVision(Substitute.For<IVisionClient>(), Substitute.For<IDiscordRest>(), this.options, NullLogger<DiscordVision>.Instance),
            Substitute.For<IConversationSessionStore>(), this.turnStore, this.corpus, this.queue, new DiscordLastTurns(), lessons, this.clock,
            this.options, NullLogger<DiscordAnswerer>.Instance);
        return new DiscordDailyCheckIn(
            answerer, this.queue, this.speech, this.channel, this.options, this.clock, NullLogger<DiscordDailyCheckIn>.Instance);
    }

    private static FrontierToolBundle Bundle()
    {
        var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
        var recall = Substitute.For<IFrontierRecall>();
        recall.Tool.Returns(new FrontierTool("recall", "r", schema));
        var remember = Substitute.For<IFrontierRemember>();
        remember.Tool.Returns(new FrontierTool("remember", "m", schema));
        var scheduling = Substitute.For<IFrontierScheduling>();
        scheduling.ScheduleTool.Returns(new FrontierTool("schedule", "s", schema));
        scheduling.ConfirmTool.Returns(new FrontierTool("confirm_schedule", "c", schema));
        var fitness = Substitute.For<IFrontierFitness>();
        fitness.SetsToolAsync(Arg.Any<CancellationToken>()).Returns(new FrontierTool("log_sets", "l", schema));
        fitness.CardioTool.Returns(new FrontierTool("log_cardio", "l", schema));
        var research = Substitute.For<IFrontierResearch>();
        research.SearchTool.Returns(new FrontierTool("search_web", "s", schema));
        research.ReadTool.Returns(new FrontierTool("read_page", "r", schema));
        var today = Substitute.For<IFrontierToday>();
        today.Tool.Returns(new FrontierTool("today", "t", schema));
        var code = Substitute.For<IFrontierCode>();
        code.Tools.Returns(Array.Empty<FrontierTool>());
        return new FrontierToolBundle(
            Substitute.For<IImageGenerator>(), Substitute.For<IPortraitGenerator>(), recall, remember, Substitute.For<IFrontierLesson>(), scheduling,
            Substitute.For<IGallerySearch>(), Substitute.For<IGalleryPictures>(), fitness, research, today,
            code, NullLogger<FrontierToolBundle>.Instance);
    }

    private static async IAsyncEnumerable<T> ManyAsync<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<T> NoneAsync<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
