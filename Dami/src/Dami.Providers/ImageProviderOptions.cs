namespace Dami.Providers;

/// <summary>Selects the image provider both tiers register, and its backup (ADR-0035).</summary>
/// <remarks>
/// Two lines in an env file — <c>Images__Provider</c> and <c>Images__Backup</c> — decide
/// which door every image request (the Gallery, the daily portrait, Discord, the
/// frontier's make_image tool) goes through, and which one retries when the first
/// produces no picture. The default is the subscription with no backup, so unset values
/// change nothing.
/// </remarks>
public sealed class ImageProviderOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Images";

    /// <summary>The door. Bound case-insensitively from its name.</summary>
    public ImageProviderKind Provider { get; set; } = ImageProviderKind.Codex;

    /// <summary>
    /// The door to retry on when <see cref="Provider"/> refuses or fails. Unset, or the
    /// same as the primary, means no retry.
    /// </summary>
    public ImageProviderKind? Backup { get; set; }
}
