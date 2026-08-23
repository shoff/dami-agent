namespace Dami.Providers;

/// <summary>The deterministic routing table.</summary>
public sealed class RoutingOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Routing";

    /// <summary>Work kinds the sidecar always handles, regardless of privacy class.</summary>
    public IList<string> LocalWorkKinds { get; } =
        ["classification", "summarization", "categorization", "extraction"];

    /// <summary>
    /// Whether a frontier provider exists to route to.
    /// </summary>
    /// <remarks>
    /// False until an Anthropic (or other) adapter is configured with credentials —
    /// which arrive out of band, never through this repository. While false, everything
    /// degrades to local rather than failing.
    /// </remarks>
    public bool FrontierEnabled { get; set; }
}
