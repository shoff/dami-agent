namespace Dami.Core.Identity;

/// <summary>Where the installed identity preamble lives.</summary>
public sealed class IdentityOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Identity";

    /// <summary>Path to the distilled identity prompt block.</summary>
    public string Path { get; set; } = "/opt/dami/identity-prompt.md";
}
