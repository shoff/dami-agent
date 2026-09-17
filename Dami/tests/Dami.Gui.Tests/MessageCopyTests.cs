using Xunit;

namespace Dami.Gui.Tests;

public sealed class MessageCopyTests
{
    [Fact]
    public void Body_Should_Enable_Copy_When_Streaming_Text_Arrives()
    {
        var message = new Message("dami", string.Empty);
        var notifications = new List<string?>();
        message.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        message.Body = "The first words";

        Assert.Equal((true, true), (message.CanCopy, notifications.Contains(nameof(Message.CanCopy))));
    }
    [Theory]
    [InlineData("", false)]
    [InlineData("Partial answer", true)]
    public void CanCopy_Should_Require_Actual_Text(string text, bool expected)
    {
        Assert.Equal(expected, new Message("dami", text).CanCopy);
    }
}
