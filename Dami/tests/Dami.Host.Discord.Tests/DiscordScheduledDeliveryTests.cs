using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
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

    private static async IAsyncEnumerable<ConversationTurn> NoneAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private DiscordScheduledDelivery Subject()
    {
        var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
        var recall = Substitute.For<IFrontierRecall>();
        recall.Tool.Returns(new FrontierTool("recall", "r", schema));
        var remember = Substitute.For<IFrontierRemember>();
        remember.Tool.Returns(new FrontierTool("remember", "m", schema));
        var scheduling = Substitute.For<IFrontierScheduling>();
        scheduling.ScheduleTool.Returns(new FrontierTool("schedule", "s", schema));
        scheduling.ConfirmTool.Returns(new FrontierTool("confirm_schedule", "c", schema));
        var options = new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true };
        this.turnStore.RecentCompletedTurnsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(NoneAsync());
        var answerer = new DiscordAnswerer(
            this.channel, this.augmented, new DiscordReplyStreamer(this.progressive),
            new FrontierToolBundle(
                Substitute.For<IImageGenerator>(), Substitute.For<IPortraitGenerator>(), recall, remember, scheduling,
                Substitute.For<IGallerySearch>(), Substitute.For<IGalleryPictures>(),
                NullLogger<FrontierToolBundle>.Instance),
            new DiscordVision(Substitute.For<IVisionClient>(), Substitute.For<IDiscordRest>(), options, NullLogger<DiscordVision>.Instance),
            Substitute.For<IConversationSessionStore>(), this.turnStore, TimeProvider.System, options,
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
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, OneAsync("here is your morning")));

        await this.Subject().DeliverAsync(Job("discord:1543678906748641310"), CancellationToken.None);

        await this.augmented.Received(1).StreamAsync(
            Arg.Is<string>(question => question.Contains("morning portrait", StringComparison.Ordinal)
                && question.Contains("starting the day", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<FrontierToolbox>(tools => tools.Tools.Any(tool => tool.Name == FrontierToolBundle.MAKE_PORTRAIT)),
            Arg.Any<CancellationToken>());
        await this.progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content => content.ConversationId == "1543678906748641310"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_Should_Refuse_A_Job_That_Is_Not_Its_Own()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().DeliverAsync(Job("gui"), CancellationToken.None));
    }
}
