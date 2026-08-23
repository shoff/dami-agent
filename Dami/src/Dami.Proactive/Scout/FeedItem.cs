namespace Dami.Proactive.Scout;

/// <summary>One entry pulled from a feed.</summary>
public sealed record FeedItem
{
    /// <summary>Creates a feed item.</summary>
    public FeedItem(string title, string link, DateTimeOffset? publishedAt)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(link);

        this.Title = title;
        this.Link = link;
        this.PublishedAt = publishedAt;
    }

    /// <summary>The entry's title.</summary>
    public string Title { get; }

    /// <summary>Where it points.</summary>
    public string Link { get; }

    /// <summary>When it was published, if the feed said.</summary>
    public DateTimeOffset? PublishedAt { get; }
}
