namespace Dami.Providers;

/// <summary>Where the local reranker sidecar listens.</summary>
public sealed class TeiRerankOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "TeiRerank";

    /// <summary>The sidecar's base address. Loopback by design.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8081";

    /// <summary>
    /// Most candidates in one request. TEI refuses more than its
    /// <c>--max-client-batch-size</c> (32 by default) with a 422; on 2026-09-05 the
    /// opportunity scout sent forty and lost its ranking. Larger sets go in batches and
    /// the raw scores are merged.
    /// </summary>
    public int MaxBatch { get; set; } = 32;
}
