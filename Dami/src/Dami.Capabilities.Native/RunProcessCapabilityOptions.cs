namespace Dami.Capabilities.Native;

/// <summary>Execution policy for the native run-process capability.</summary>
public sealed class RunProcessCapabilityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "RunProcess";

    /// <summary>Gets or sets the fixed process working directory.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets aliases mapped to absolute executable paths.</summary>
    public IReadOnlyDictionary<string, string> AllowedExecutables { get; set; }
        = new Dictionary<string, string>();

    /// <summary>Gets or sets the combined stdout/stderr byte limit.</summary>
    public int MaxOutputBytes { get; set; } = 64 * 1024;
}
