using Xunit;

namespace Dami.Gui.Tests;

public sealed class ChatImageInputTests
{
    [Theory]
    [InlineData("photo.png", true)]
    [InlineData("PHOTO.JPEG", true)]
    [InlineData("notes.txt", false)]
    public void RecognizesSupportedImageFiles(string name, bool expected)
    {
        Assert.Equal(expected, ChatImageInput.IsSupportedFile(name));
    }

    [Fact]
    public void SuppliesAQuestionWhenAnImageIsSentWithoutText()
    {
        Assert.Equal("What is in this image?", ChatImageInput.MessageOrDefault(null, true));
        Assert.Null(ChatImageInput.MessageOrDefault(null, false));
    }
}
