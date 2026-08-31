namespace Dami.Proactive.Security;

/// <summary>The CVE watch's sources (H11).</summary>
public sealed class CveWatchOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "CveWatch";

    /// <summary>Ubuntu Security Notices, pulled whole — no query carries anything.</summary>
    public string UsnFeedUrl { get; set; } = "https://ubuntu.com/security/notices/rss.xml";

    /// <summary>The GitHub advisory database. Package names ride the query; versions never do.</summary>
    public string AdvisoriesUrl { get; set; } = "https://api.github.com/advisories";

    /// <summary>Where the NuGet closure is read from (project.assets.json files).</summary>
    public string RepositoryRoot { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>Package names per advisories query.</summary>
    public int QueryChunk { get; set; } = 20;

    /// <summary>Confidence carried by each surfacing.</summary>
    public double Confidence { get; set; } = 0.75;
}
