using Xunit;

namespace Dami.Gui.Tests;

public sealed class MessageRecoveryTests
{
    [Fact]
    public void RecoverableDraft_Should_Notify_The_View_When_Recovery_Becomes_Available()
    {
        var reply = new Message("dami", "partial answer");
        var changed = new List<string?>();
        reply.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        reply.RecoverableDraft = new ChatDraft("question", []);

        Assert.Contains(nameof(Message.CanRecover), changed);
    }
}
