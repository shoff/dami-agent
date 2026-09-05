using System.Text.Json;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class DirectChatRequestTests
{
    [Fact]
    public void SerializesEveryDirectMessageAsFrontierWithoutAugmentation()
    {
        var json = JsonSerializer.Serialize(new DirectChatRequest("hello"));

        using var document = JsonDocument.Parse(json);
        Assert.Equal("hello", document.RootElement.GetProperty("message").GetString());
        Assert.True(document.RootElement.GetProperty("frontier").GetBoolean());
        Assert.False(document.RootElement.GetProperty("augmented").GetBoolean());
    }

    [Fact]
    public void SerializesMultipleImagesForTheLoopbackRuntime()
    {
        var request = new DirectChatRequest("what is this?")
        {
            Images =
            [
                new DirectChatImage("first.png", "image/png", [1]),
                new DirectChatImage("second.png", "image/png", [2]),
            ],
        };

        var json = JsonSerializer.Serialize(request);

        using var document = JsonDocument.Parse(json);
        var images = document.RootElement.GetProperty("images").EnumerateArray().ToArray();
        Assert.Equal("first.png", images[0].GetProperty("fileName").GetString());
        Assert.Equal("second.png", images[1].GetProperty("fileName").GetString());
    }

    [Fact]
    public void PendingReplyDoesNotPrintProviderRouting()
    {
        Assert.Empty(DirectChatPresentation.PENDING_META);
    }
}
