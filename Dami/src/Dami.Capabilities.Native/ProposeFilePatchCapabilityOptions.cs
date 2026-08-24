namespace Dami.Capabilities.Native;

/// <summary>Safety bounds for approval-gated file patch proposals.</summary>
public sealed class ProposeFilePatchCapabilityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "FilePatch";

    /// <summary>Gets or sets the only directory beneath which targets may exist.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum current or replacement UTF-8 byte count.</summary>
    public int MaxBytes { get; set; } = 1024 * 1024;
}
