namespace Dami.Proactive.Releases;

/// <summary>One release source worth watching, and why.</summary>
public sealed class ReleaseWatch
{
    /// <summary>What to call it in facts and surfacings.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The source URL. Its host must be on the egress allowlist.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary><c>feed</c> (RSS or Atom) or <c>nvidia-latest</c> (the latest.txt one-liner).</summary>
    public string Kind { get; set; } = "feed";

    /// <summary>The version running here. Empty means learn silently on first sight.</summary>
    public string Baseline { get; set; } = string.Empty;

    /// <summary>Why it is watched. Rides into the surfacing so the reader knows the stakes.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The fix-release watch's sources (H13). Defaults are the software whose defects have
/// actually bitten this machine, with the versions verified installed on 2026-08-30.
/// </summary>
public sealed class ReleaseWatchOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "ReleaseWatch";

    /// <summary>Sources to read each pass.</summary>
    public IList<ReleaseWatch> Watches { get; } =
    [
        new ReleaseWatch
        {
            Name = "nvidia-driver",
            Url = "https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt",
            Kind = "nvidia-latest",
            Baseline = "595.84",
            Reason = "595.84's libnvidia-glcore segfault crashes Dami.Gui (2026-08-28)",
        },
        new ReleaseWatch
        {
            Name = "dotnet-sdk",
            Url = "https://github.com/dotnet/sdk/releases.atom",
            Baseline = "10.0.400",
        },
        new ReleaseWatch
        {
            Name = "postgresql",
            Url = "https://www.postgresql.org/versions.rss",
            Baseline = "16.15",
            Reason = "ADR-0016 proposes the 17 migration; point releases matter meanwhile",
        },
        new ReleaseWatch
        {
            Name = "ollama",
            Url = "https://github.com/ollama/ollama/releases.atom",
        },
        new ReleaseWatch
        {
            Name = "avalonia",
            Url = "https://github.com/AvaloniaUI/Avalonia/releases.atom",
            Baseline = "12.1.1",
        },
    ];

    /// <summary>Seconds between sources; a courtesy to rate limits.</summary>
    public int WatchDelaySeconds { get; set; } = 2;

    /// <summary>Confidence carried by each surfacing.</summary>
    public double Confidence { get; set; } = 0.65;
}
