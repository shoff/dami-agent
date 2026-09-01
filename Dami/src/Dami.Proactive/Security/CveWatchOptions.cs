namespace Dami.Proactive.Security;

/// <summary>The CVE watch's sources (H11).</summary>
public sealed class CveWatchOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "CveWatch";

    /// <summary>Ubuntu Security Notices, pulled whole — no query carries anything.</summary>
    public string UsnFeedUrl { get; set; } = "https://ubuntu.com/security/notices/rss.xml";

    /// <summary>
    /// The GitHub advisory database. The query carries an ecosystem and a publication
    /// window and nothing else — matching happens on this host.
    /// </summary>
    public string AdvisoriesUrl { get; set; } = "https://api.github.com/advisories";

    /// <summary>Where the NuGet closure is read from (project.assets.json files).</summary>
    public string RepositoryRoot { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>Advisories per page.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Pages read per pass. Bounds the pull without a silent truncation.</summary>
    public int MaxPages { get; set; } = 3;

    /// <summary>How far back the advisory window reaches.</summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>Confidence carried by each surfacing.</summary>
    public double Confidence { get; set; } = 0.75;
}
