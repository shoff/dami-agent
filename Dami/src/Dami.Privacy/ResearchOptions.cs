namespace Dami.Privacy;

/// <summary>Research egress (ADR-0033): read-only reading of public pages.</summary>
/// <remarks>
/// Off by default. This is the one door through the boundary that is not host-listed,
/// and it was widened by Steve's decision on 2026-09-05; it is switched on deliberately
/// in the drop-in rather than inherited by anyone who deploys.
/// </remarks>
public sealed class ResearchOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Research";

    /// <summary>Whether pages may be read at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hosts that are never read, whatever the search returned.</summary>
    public IList<string> BlockedHosts { get; } = [];

    /// <summary>Most bytes accepted from one page.</summary>
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Most characters of readable text handed back from one page.</summary>
    public int MaxTextChars { get; set; } = 8000;

    /// <summary>Per-page deadline.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}
