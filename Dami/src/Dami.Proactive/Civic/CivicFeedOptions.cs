namespace Dami.Proactive.Civic;

/// <summary>One public civic feed and the fact category its items become.</summary>
public sealed class CivicFeed
{
    /// <summary>What to call it as a fact source.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The feed URL. Its host must be on the egress allowlist.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The category recorded for every item: <c>notice</c>, <c>meeting</c>, …</summary>
    public string Category { get; set; } = "notice";
}

/// <summary>
/// The civic domain's feeds. Defaults are Lakeville, MN — the city the corpus places Steve
/// in — and were verified to be live RSS on 2026-08-25. Edit them when they are wrong.
/// </summary>
public sealed class CivicFeedOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "CivicFeeds";

    /// <summary>Feeds to read each pass.</summary>
    public IList<CivicFeed> Feeds { get; } =
    [
        new CivicFeed
        {
            Name = "lakeville-news",
            Url = "https://www.lakevillemn.gov/RSSFeed.aspx?ModID=1&CID=All-newsflash.xml",
            Category = "notice",
        },
        new CivicFeed
        {
            Name = "lakeville-calendar",
            Url = "https://www.lakevillemn.gov/RSSFeed.aspx?ModID=58&CID=All-calendar.xml",
            Category = "meeting",
        },
    ];

    /// <summary>Seconds between feeds on one host; a courtesy to rate limits.</summary>
    public int FeedDelaySeconds { get; set; } = 2;
}
