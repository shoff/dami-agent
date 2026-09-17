using Xunit;

namespace Dami.Gui.Tests;

public sealed class ChatAttachmentTests
{
    [Theory]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, true)]
    public void StageImage_Should_Validate_Pixels_Before_Changing_The_Draft(bool valid, int count, bool rejected)
    {
        var state = new WindowState();
        state.PendingImages.Add(new PendingChatImage(new DirectChatImage("kept.png", "image/png", [1])));
        var bytes = valid
            ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNYvfT/fwAHnQNPpzrMjQAAAABJRU5ErkJggg==")
            : new byte[] { 1, 2, 3 };
        var picture = new PendingChatImage(new DirectChatImage("new.png", "image/png", bytes));
        var error = Record.Exception(() => state.StageImage(picture));

        Assert.Equal((count, rejected), (state.PendingImages.Count, error is ArgumentException));
    }

    [Fact]
    public void Message_Should_Retain_Its_Attachments_After_The_Composer_Is_Cleared()
    {
        var state = new WindowState();
        var picture = new PendingChatImage(new DirectChatImage("photo.png", "image/png", [1]));
        state.PendingImages.Add(picture);
        var message = new Message("you", "Take a look", state.PendingImages);
        state.PendingImages.Clear();

        Assert.Same(picture, Assert.Single(message.Attachments));
    }

    [Fact]
    public void RemovePendingImage_Should_Remove_Only_The_Selected_Attachment_With_A_Duplicate_Name()
    {
        var state = new WindowState();
        var first = new PendingChatImage(new DirectChatImage("photo.png", "image/png", [1]));
        var second = new PendingChatImage(new DirectChatImage("photo.png", "image/png", [2]));
        state.PendingImages.Add(first);
        state.PendingImages.Add(second);
        state.RemovePendingImage(first);

        Assert.Same(second, Assert.Single(state.PendingImages));
    }
}
