namespace Dami.Capabilities.Native;

/// <summary>Filesystem bounds for the native read-file capability.</summary>
public sealed class ReadFileCapabilityOptions
{
    /// <summary>Gets or sets the only directory tree visible to the capability.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum file size returned to a caller.</summary>
    public int MaxBytes { get; set; } = 1024 * 1024;
}
