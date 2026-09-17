using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Xunit;

namespace Dami.Gui.Tests;

[Collection("Reply controls")]
public sealed class ReplyBlockViewTests
{
    [Fact]
    public async Task Constructor_Should_Reject_A_Null_Block()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new ReplyBlockView(null!));
        });
    }

    [Fact]
    public async Task Update_Should_Keep_Code_Copy_Payload_Current_While_Streaming()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ReplyBlockView(new ReplyBlock("code", "let ", "js"));
            view.Update(new ReplyBlock("code", "let x = 1;\n", "js"));
            var copy = view.GetLogicalDescendants().OfType<Button>().Single();

            Assert.Equal(("copy-code", "let x = 1;\n"), (copy.Tag, copy.CommandParameter));
        });
    }

    [Fact]
    public async Task Constructor_Should_Allow_The_Code_Scroller_To_Present_Its_Content()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ReplyBlockView(new ReplyBlock("code", "print('hello')", "py"));
            var scroll = view.GetLogicalDescendants().OfType<ScrollViewer>().Single();
            scroll.Template = new FuncControlTemplate<ScrollViewer>((owner, _) => new ContentPresenter { Content = owner.Content });
            scroll.ApplyTemplate();

            Assert.IsType<SelectableTextBlock>(scroll.Content);
        });
    }

    [Fact]
    public async Task Constructor_Should_Assign_A_Logical_Parent_To_Every_Code_Child()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ReplyBlockView(new ReplyBlock("code", "print('hello')", "py"));

            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Avalonia.StyledElement>(), child => child.Parent is null);
        });
    }
}
