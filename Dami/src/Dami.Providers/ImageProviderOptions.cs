namespace Dami.Providers;

/// <summary>Selects the image provider both tiers register (ADR-0035).</summary>
/// <remarks>
/// One line in a drop-in — <c>Images__Provider=Gemini</c> — moves every image request
/// (the Gallery, the daily portrait, Discord, the frontier's make_image tool) onto the
/// named door. The default is the subscription, so an unset value changes nothing.
/// </remarks>
public sealed class ImageProviderOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Images";

    /// <summary>The door. Bound case-insensitively from its name.</summary>
    public ImageProviderKind Provider { get; set; } = ImageProviderKind.Codex;
}
