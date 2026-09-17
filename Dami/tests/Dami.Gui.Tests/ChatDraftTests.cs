using Xunit;

namespace Dami.Gui.Tests;

public sealed class ChatDraftTests
{
    [Fact]
    public void Restore_Should_Recover_The_Original_Text_And_Images_Into_An_Empty_Composer()
    {
        var original = new ChatDraft("  question\nsecond line  ",
            [new DirectChatImage("one.png", "image/png", [1]), new DirectChatImage("two.png", "image/png", [2])]);

        Assert.Same(original, original.Restore(new ChatDraft(string.Empty, [])));
    }

    [Fact]
    public void Constructor_Should_Keep_Attachments_When_The_Composer_Collection_Is_Cleared()
    {
        var image = new DirectChatImage("one.png", "image/png", [1]);
        var images = new List<DirectChatImage> { image };
        var draft = new ChatDraft("question", images);

        images.Clear();

        Assert.Equal([image], draft.Images);
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Text()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatDraft(null!, []));
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Images()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatDraft("question", null!));
    }

    [Fact]
    public void Restore_Should_Preserve_New_Attachments_Without_Text()
    {
        var failed = new ChatDraft("first question", []);
        var current = new ChatDraft(string.Empty, [new DirectChatImage("next.png", "image/png", [1])]);

        Assert.Same(current, failed.Restore(current));
    }

    [Fact]
    public void Restore_Should_Preserve_A_Newer_Draft()
    {
        var failed = new ChatDraft("first question", []);
        var current = new ChatDraft("my next question", []);

        Assert.Same(current, failed.Restore(current));
    }
}
