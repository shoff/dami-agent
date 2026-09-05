namespace Dami.Providers;

/// <summary>The private SearXNG on loopback (ADR-0033).</summary>
public sealed class SearxngOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Searxng";

    /// <summary>Where it listens. Loopback only; it does its own egress to the engines.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8888";

    /// <summary>Per-search deadline.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}
