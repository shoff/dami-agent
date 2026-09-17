using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Gallery;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordScheduledDeliveryTests
{
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly IProgressiveEgressChannel progressive = Substitute.For<IProgressiveEgressChannel>();
    private readonly IAugmentedTurn augmented = Substitute.For<IAugmentedTurn>();
    private readonly IConversationTurnStore turnStore = Substitute.For<IConversationTurnStore>();

    private static ScheduledJob Job(string? delivery) => new(
        Guid.NewGuid(), "morning portrait", "d", ScheduledJobKind.Prompt,
        "make a picture of yourself starting the day", [], "0 7 * * *", "America/Chicago",
        ScheduledJobStatus.Active, DateTimeOffset.UnixEpoch, null, null, null, null, delivery);

    private static async IAsyncEnumerable<string> OneAsync(string text)
    {
        yield return text;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Surfacing> NoSurfacingsAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<ConversationTurn> NoneAsync()
    {
        await Task.CompletedTask;
        yield break;
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

    private DiscordScheduledDelivery Subject()
    {
        var queue = Substitute.For<ISurfacingQueue>();
        queue.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(NoSurfacingsAsync());
        var lessons = Substitute.For<IStandingLessons>();
        lessons.LinesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var options = new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true };
        this.turnStore.RecentCompletedTurnsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(NoneAsync());
        var answerer = new DiscordAnswerer(
            this.channel, this.augmented, new DiscordReplyStreamer(this.progressive),
            Bundle(),
            new DiscordVision(Substitute.For<IVisionClient>(), Substitute.For<IDiscordRest>(), options, NullLogger<DiscordVision>.Instance),
            Substitute.For<IConversationSessionStore>(), this.turnStore, queue, lessons, TimeProvider.System, options,
            NullLogger<DiscordAnswerer>.Instance);
        return new DiscordScheduledDelivery(answerer);
    }

    [Theory]
    [InlineData("discord:1543678906748641310", true)]
    [InlineData("discord:", false)]
    [InlineData("gui", false)]
    [InlineData(null, false)]
    public void Handles_Only_A_Discord_Channel_Key(string? delivery, bool expected)
    {
        Assert.Equal(expected, this.Subject().Handles(delivery));
    }

    [Fact]
    public async Task Deliver_Should_Run_The_Request_As_A_Turn_In_That_Channel_With_The_Bundle()
    {
        this.progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>()).Returns("m1");
        this.augmented.StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, OneAsync("here is your morning")));

        await this.Subject().DeliverAsync(Job("discord:1543678906748641310"), CancellationToken.None);

        await this.augmented.Received(1).StreamAsync(
            Arg.Is<string>(question => question.Contains("morning portrait", StringComparison.Ordinal)
                && question.Contains("starting the day", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<FrontierToolbox>(tools => tools.Tools.Any(tool => tool.Name == FrontierToolBundle.MAKE_PORTRAIT)),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await this.progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content => content.ConversationId == "1543678906748641310"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_Should_Fail_The_Run_When_The_Channel_Refused_The_Answer()
    {
        // 2026-09-08: four portraits in a row were refused into Steve's DM and every one
        // was recorded as Succeeded, because the refusal was explained and swallowed. The
        // conversation still gets the explanation; the job gets the failure.
        this.progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new EgressRefusedException(
                "discord refused profile-derived content addressed to someone other than its subject (ADR-0025)."));
        this.augmented.StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, OneAsync("a portrait")));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.Subject().DeliverAsync(Job("discord:1543678906748641310"), CancellationToken.None));

        Assert.Contains("ADR-0025", failure.Message, StringComparison.Ordinal);
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("ADR-0025", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_Should_Refuse_A_Job_That_Is_Not_Its_Own()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().DeliverAsync(Job("gui"), CancellationToken.None));
    }
}
