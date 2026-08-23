namespace Dami.Providers;

/// <summary>Where the local TEI embedding sidecar listens.</summary>
public sealed class TeiOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Tei";

    /// <summary>The sidecar's base address. Loopback by design — this is local inference.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>TEI's max_client_batch_size; requests are chunked to it.</summary>
    public int BatchSize { get; set; } = 32;
}
