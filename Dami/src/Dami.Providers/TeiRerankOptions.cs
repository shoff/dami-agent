namespace Dami.Providers;

/// <summary>Where the local reranker sidecar listens.</summary>
public sealed class TeiRerankOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "TeiRerank";

    /// <summary>The sidecar's base address. Loopback by design.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8081";
}
