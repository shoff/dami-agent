namespace Dami.Proactive.Scout;

/// <summary>The scout's feeds, interests, and thresholds.</summary>
public sealed class InterestScoutOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "InterestScout";

    /// <summary>Feed URLs to scan. Every fetch goes through the egress boundary.</summary>
    public IList<string> Feeds { get; } = [];

    /// <summary>
    /// Interest statements, in plain language, embedded locally.
    /// </summary>
    /// <remarks>
    /// This is the taste model's seed and it never leaves the host — only the feed
    /// fetches cross the boundary, and they carry nothing derived from this list.
    /// </remarks>
    public IList<string> Interests { get; } = [];

    /// <summary>Cosine similarity an item must reach to surface. A guess, tuned on feedback.</summary>
    public double SurfaceThreshold { get; set; } = 0.55;

    /// <summary>The most items one pass may surface, before the queue's own daily cap.</summary>
    public int MaxItemsPerPass { get; set; } = 3;
}
