using Xunit;

namespace Dami.Gui.Tests;

public sealed class ImageGenerationPromptTests
{
    [Theory]
    [InlineData("/image a moonlit lake", "a moonlit lake")]
    [InlineData("generate an image of a moonlit lake", "a moonlit lake")]
    [InlineData("hello", null)]
    public void ExtractsOnlyExplicitImageRequests(string message, string? expected)
    {
        Assert.Equal(expected, ImageGenerationPrompt.Extract(message));
    }

    [Theory]
    [InlineData("give me a picture of yourself doing yoga", "doing yoga")]
    [InlineData("send me a photo of you making coffee", "making coffee")]
    [InlineData("surprise me with what you are doing", "surprise")]
    [InlineData("tell me what you are doing", null)]
    public void RecognizesNaturalDamiPortraitRequests(string message, string? expected)
    {
        var scene = ImageGenerationPrompt.DamiScene(message);

        if (expected == "surprise")
        {
            Assert.Contains("surprise", scene, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(expected, scene);
        }
    }
}
