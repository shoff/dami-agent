namespace Dami.Providers;

/// <summary>The OpenAI images API (ADR-0027).</summary>
/// <remarks>
/// The key arrives through secret configuration — <c>OpenAiImages__ApiKey</c>, two
/// underscores — and is never in the repository, in appsettings, or in a trace. Its
/// absence disables the capability rather than failing calls.
/// </remarks>
public sealed class OpenAiImageOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "OpenAiImages";

    /// <summary>API endpoint. Its host must ALSO be on the egress allowlist.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>The key. Empty means the capability is absent.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The model. `gpt-image-1` is what the Hermes jobs used.</summary>
    public string Model { get; set; } = "gpt-image-1";
}
