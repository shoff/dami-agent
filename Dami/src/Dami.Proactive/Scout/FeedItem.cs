namespace Dami.Proactive.Scout;

/// <summary>One entry pulled from a feed.</summary>
public sealed record FeedItem
{
    /// <summary>Creates a feed item.</summary>
    public FeedItem(string title, string link, DateTimeOffset? publishedAt, string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(link);

        this.Title = title;
        this.Link = link;
        this.PublishedAt = publishedAt;
        this.Summary = summary;
    }

    /// <summary>The item's description or summary, raw, when the feed carries one.</summary>
    public string? Summary { get; }

    /// <summary>The entry's title.</summary>
    public string Title { get; }

    /// <summary>Where it points.</summary>
    public string Link { get; }

    /// <summary>When it was published, if the feed said.</summary>
    public DateTimeOffset? PublishedAt { get; }
}
