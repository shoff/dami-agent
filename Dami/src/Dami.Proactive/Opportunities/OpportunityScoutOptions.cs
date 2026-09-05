namespace Dami.Proactive.Opportunities;

/// <summary>The weekly opportunity scout (ADR-0033): what to look for, and who it is for.</summary>
/// <remarks>
/// Off by default, and quiet until the drop-in names queries. The queries and the
/// profile are Steve's own words, written by him, which is why they may leave the host
/// through the search engine without a gate — the same reasoning as the portrait prompt.
/// </remarks>
public sealed class OpportunityScoutOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "OpportunityScout";

    /// <summary>Whether the pass runs at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Who this is for, in a paragraph: skills, assets, what counts as worth his time.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>The searches to run each pass.</summary>
    public IList<string> Queries { get; } = [];

    /// <summary>Hits kept per query before ranking.</summary>
    public int ResultsPerQuery { get; set; } = 10;

    /// <summary>How many make the digest.</summary>
    public int MaxSurfaced { get; set; } = 8;

    /// <summary>Confidence carried by the surfacing.</summary>
    public double Confidence { get; set; } = 0.6;
}
