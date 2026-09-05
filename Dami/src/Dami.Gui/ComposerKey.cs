using Avalonia.Input;

namespace Dami.Gui;

/// <summary>Keyboard behavior for the multiline direct-chat composer.</summary>
public static class ComposerKey
{
    /// <summary>Plain Enter sends; Shift+Enter remains an editor newline.</summary>
    public static bool ShouldSend(Key key, KeyModifiers modifiers) =>
        key == Key.Enter && !modifiers.HasFlag(KeyModifiers.Shift);

    /// <summary>Inserts one newline, replacing the selected range when present.</summary>
    public static (string Text, int Caret) InsertNewline(string text, int selectionStart, int selectionEnd)
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        return (text[..start] + "\n" + text[end..], start + 1);
    }
}
