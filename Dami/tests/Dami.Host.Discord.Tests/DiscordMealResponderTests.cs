using Dami.Contracts.Models;
using Dami.Contracts.Nutrition;
using Dami.Contracts.Privacy;
using Dami.Core.Nutrition;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>A meal photo becomes an estimate in the meal log, on this host, confirmed in Steve's DM (D6).</summary>
public sealed class DiscordMealResponderTests
{
    private static readonly DateTimeOffset received = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    private readonly IVisionClient vision = Substitute.For<IVisionClient>();
    private readonly IDiscordRest rest = Substitute.For<IDiscordRest>();
    private readonly IMealLog meals = Substitute.For<IMealLog>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();

    public DiscordMealResponderTests()
    {
        this.rest.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1 });
        this.meals.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new Meal(Guid.NewGuid(), received.AddHours(-5), "oatmeal", 770, 55, received),
                      new Meal(Guid.NewGuid(), received, "chicken burrito", 650, 40, received)]);
    }

    private static InboundMessage Photo(string text) =>
        new("owner", "dm-7", text, received) { Attachments = [new InboundAttachment("m.jpg", "https://cdn/m.jpg", "image/jpeg", 500_000)] };

    private DiscordMealResponder Subject() => new(
        this.vision, this.rest, this.meals, this.channel, new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true },
        new FakeTimeProvider(received), NullLogger<DiscordMealResponder>.Instance);

    [Fact]
    public async Task A_Meal_Photo_Should_Be_Estimated_Logged_And_Totalled_For_The_Day()
    {
        this.vision.DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), MealReader.PROMPT, Arg.Any<CancellationToken>())
            .Returns("""{"food":"chicken burrito","calories":650,"protein":40}""");

        Assert.True(await this.Subject().TryAnswerAsync(Photo("lunch"), CancellationToken.None));

        await this.meals.Received(1).RecordAsync(
            Arg.Is<Meal>(meal => meal.Description == "chicken burrito" && meal.Calories == 650 && meal.ProteinGrams == 40),
            Arg.Any<CancellationToken>());
        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("about 650 kcal and 40 g protein (chicken burrito)", StringComparison.Ordinal)
                && content.Text.Contains("Today so far: 1,420 kcal, 95 g protein", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("what is this")]
    [InlineData("receipt")]
    public async Task Other_Photos_Should_Go_Elsewhere(string text)
    {
        Assert.False(await this.Subject().TryAnswerAsync(Photo(text), CancellationToken.None));
    }

    [Fact]
    public async Task A_Photo_That_Is_Not_Readable_As_Food_Should_Be_Said_And_Not_Logged()
    {
        this.vision.DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), MealReader.PROMPT, Arg.Any<CancellationToken>()).Returns("{}");

        Assert.True(await this.Subject().TryAnswerAsync(Photo("dinner"), CancellationToken.None));

        await this.meals.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }
}
