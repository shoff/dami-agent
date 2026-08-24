namespace Dami.Capabilities.Skills;

/// <summary>Bounds filesystem skill discovery and content hashing.</summary>
public sealed class SkillLoaderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "Skills";

    /// <summary>Gets or sets the directory containing one folder per skill.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of skill folders.</summary>
    public int MaxSkills { get; set; } = 256;

    /// <summary>Gets or sets the maximum UTF-8 descriptor size.</summary>
    public int MaxDescriptorBytes { get; set; } = 64 * 1024;

    /// <summary>Gets or sets the maximum UTF-8 body size.</summary>
    public int MaxBodyBytes { get; set; } = 256 * 1024;

    /// <summary>Gets or sets the maximum number of bundled references per skill.</summary>
    public int MaxReferences { get; set; } = 32;

    /// <summary>Gets or sets the maximum combined reference bytes per skill.</summary>
    public int MaxReferenceBytes { get; set; } = 2 * 1024 * 1024;
}
