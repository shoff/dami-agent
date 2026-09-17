using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace Dami.Gui.Tests;

[Collection("Reply controls")]
public sealed class ReplyViewTests
{
    [Fact]
    public async Task Text_Should_Keep_Existing_Blocks_When_The_Final_Block_Grows()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ReplyView { Text = "## First\n\n```cs\nvar " };
            var first = view.Children[0];
            var code = view.Children[1];
            view.Text += "x = 1;";
            var copy = code.GetLogicalDescendants().OfType<Button>().Single();

            Assert.Equal((first, code, "var x = 1;"), (view.Children[0], view.Children[1], copy.CommandParameter));
        });
    }

    [Fact]
    public async Task Text_Should_Append_A_Quote_After_A_Character_Streamed_Code_Fence()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var reply = "## Observatory\n\nA **bright** sky.\n\n- Align\n- Focus\n- Check\n\n```python\ndef observe():\n    return \"starlight\"\n```\n\n> Clear skies";
            var view = new ReplyView();
            for (var length = 1; length <= reply.Length; length++)
            {
                view.Text = reply[..length];
            }

            Assert.Equal(7, view.Children.Count);
        });
    }

    [Fact]
    public async Task DataContext_Should_Render_Code_And_Subsequent_Text_From_A_Bound_Message()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var message = new Message("dami", string.Empty);
            var view = new ReplyView { DataContext = message };
            var reply = "## Observatory\n\nA clear sky.\n\n```python\ndef observe():\n    return \"starlight\"\n```\n\n> Clear skies";
            for (var length = 1; length <= reply.Length; length++)
            {
                message.Body = reply[..length];
            }

            Assert.Equal(4, view.Children.Count);
        });
    }
}
