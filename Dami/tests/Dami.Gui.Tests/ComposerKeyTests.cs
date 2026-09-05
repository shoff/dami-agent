using Avalonia.Input;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ComposerKeyTests
{
    [Theory]
    [InlineData(KeyModifiers.None, true)]
    [InlineData(KeyModifiers.Shift, false)]
    public void EnterSendsUnlessShiftIsHeld(KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, ComposerKey.ShouldSend(Key.Enter, modifiers));
    }

    [Theory]
    [InlineData("hello", 5, 5, "hello\n", 6)]
    [InlineData("hello", 1, 4, "h\no", 2)]
    public void NewlineInsertsAtCaretOrReplacesSelection(
        string text, int start, int end, string expectedText, int expectedCaret)
    {
        Assert.Equal(
            (expectedText, expectedCaret),
            ComposerKey.InsertNewline(text, start, end));
    }
}
