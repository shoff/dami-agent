namespace Dami.Providers;

/// <summary>The Gemini image API (ADR-0035).</summary>
/// <remarks>
/// The key arrives through secret configuration — <c>GeminiImages__ApiKey</c>, two
/// underscores — and is never in the repository, in appsettings, or in a trace. Its
/// absence disables the capability rather than failing calls. The host must ALSO be on
/// the egress allowlist; being configured here exempts nothing.
/// </remarks>
public sealed class GeminiImageOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "GeminiImages";

    /// <summary>API endpoint. Its host must ALSO be on the egress allowlist.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>The key. Empty means the capability is absent.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The image-capable Gemini model, as named in <c>models/{model}:generateContent</c>.</summary>
    public string Model { get; set; } = "gemini-3.1-flash-image";

    /// <summary>
    /// Output resolution in Gemini's vocabulary (<c>512px</c>, <c>1K</c>, <c>2K</c>, <c>4K</c>).
    /// Empty lets the model decide. <see cref="Dami.Contracts.Models.ImageRequest.Quality"/>
    /// is OpenAI vocabulary and does not map; the pixel count is chosen here instead.
    /// </summary>
    public string ImageSize { get; set; } = "1K";
}
