namespace Dami.Gui;

/// <summary>Finds messages in the current conversation without modifying its contents.</summary>
public sealed class ConversationFinder
{
    private readonly List<Message> matches = [];
    private string query = string.Empty;

    /// <summary>The number of matching messages.</summary>
    public int Count => this.matches.Count;

    /// <summary>The zero-based selected result, or -1 when there are no matches.</summary>
    public int Position { get; private set; } = -1;

    /// <summary>The currently selected matching message.</summary>
    public Message? Selected => this.Position < 0 ? null : this.matches[this.Position];

    /// <summary>Searches message bodies using a literal case-insensitive phrase.</summary>
    public void Search(IEnumerable<Message> messages, string? query)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var previous = this.Selected;
        this.matches.Clear();
        var phrase = query?.Trim() ?? string.Empty;
        if (phrase.Length > 0)
        {
            foreach (var message in messages)
            {
                if (message.Body.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    this.matches.Add(message);
                }
            }
        }

        var retained = string.Equals(phrase, this.query, StringComparison.OrdinalIgnoreCase) && previous is not null
            ? this.matches.IndexOf(previous) : -1;
        this.Position = retained >= 0 ? retained : this.matches.Count == 0 ? -1 : 0;
        this.query = phrase;
    }

    /// <summary>Moves forward or backward through results, wrapping at either end.</summary>
    public void Move(int direction)
    {
        if (this.Count > 0)
        {
            this.Position = (int)(((long)this.Position + direction) % this.Count + this.Count) % this.Count;
        }
    }
}
