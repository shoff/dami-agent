namespace Dami.Contracts.Context;

/// <summary>One piece of retrieved context, with the provenance §9.2 requires.</summary>
public sealed record RetrievedItem
{
    /// <summary>Creates a retrieved item.</summary>
    public RetrievedItem(string kind, Guid sourceId, string content, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(content);

        this.Kind = kind;
        this.SourceId = sourceId;
        this.Content = content;
        this.AsOf = asOf;
    }

    /// <summary>What it is — "observation", "belief".</summary>
    public string Kind { get; }

    /// <summary>The row it came from, so "why is this in my prompt" is answerable.</summary>
    public Guid SourceId { get; }

    /// <summary>The text that will enter the prompt.</summary>
    public string Content { get; }

    /// <summary>When the underlying fact was recorded or concluded.</summary>
    public DateTimeOffset AsOf { get; }
}
