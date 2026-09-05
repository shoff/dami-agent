using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordImageIntentTests
{
    [Theory]
    [InlineData("Let's see an image of you painting your toes", "Dami painting her toes")]
    [InlineData("show me a picture of yourself at the beach", "Dami at the beach")]
    [InlineData("can I get a photo of you?", "A candid portrait of Dami looking directly at Steve.")]
    [InlineData("send me a pic of Dami reading in bed.", "Dami reading in bed")]
    [InlineData("send a selfie from the gym", "Dami taking a selfie from the gym")]
    public void A_Request_For_A_Picture_Of_Dami_Should_Be_A_Portrait(string text, string scene)
    {
        var request = DiscordImageIntent.Classify(text);

        Assert.NotNull(request);
        Assert.True(request.OfDami);
        Assert.Equal(scene, request.Text);
    }

    [Theory]
    [InlineData("create an image of a red barn at sunset", "a red barn at sunset")]
    [InlineData("/image a fox in snow", "a fox in snow")]
    [InlineData("can you draw a picture of a 1/72 Spitfire?", "a 1/72 Spitfire")]
    [InlineData("show me a photo of the Eiffel tower at night", "the Eiffel tower at night")]
    public void A_Request_For_Any_Other_Picture_Should_Be_Drawn_As_Asked(string text, string prompt)
    {
        var request = DiscordImageIntent.Classify(text);

        Assert.NotNull(request);
        Assert.False(request.OfDami);
        Assert.Equal(prompt, request.Text);
    }

    [Theory]
    [InlineData("what do you think of this picture?")]
    [InlineData("can you see the image I sent?")]
    [InlineData("I like that photo")]
    [InlineData("where was I on tuesday")]
    [InlineData("the picture quality on my monitor is bad")]
    [InlineData("/image")]
    public void Talk_About_Pictures_Should_Not_Be_A_Request_For_One(string text)
    {
        Assert.Null(DiscordImageIntent.Classify(text));
    }
}
