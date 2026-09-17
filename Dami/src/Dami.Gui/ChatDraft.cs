namespace Dami.Gui;

/// <summary>A recoverable snapshot of the text and attachments submitted together.</summary>
public sealed class ChatDraft
{
    /// <summary>Captures the composer before sending.</summary>
    public ChatDraft(string text, IReadOnlyList<DirectChatImage> images)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(images);
        this.Text = text;
        this.Images = images.ToArray();
    }

    /// <summary>The original text, including whitespace and line breaks.</summary>
    public string Text { get; }

    /// <summary>The attachments in their submitted order.</summary>
    public IReadOnlyList<DirectChatImage> Images { get; }

    /// <summary>Restores this snapshot only when the current draft is empty.</summary>
    public ChatDraft Restore(ChatDraft current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.Text.Length == 0 && current.Images.Count == 0 ? this : current;
    }
}
