namespace Dami.Host;

/// <summary>Host-owned configuration for approved sandboxed tools.</summary>
public sealed class SandboxedToolHostOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "SandboxedTools";

    /// <summary>Gets or sets the private immutable runtime root.</summary>
    public string? RootDirectory { get; set; }

    /// <summary>Gets or sets the bounded startup recovery batch size.</summary>
    public int RecoveryBatchSize { get; set; } = 1_000;

    /// <summary>Gets or sets the owning user's systemd runtime directory.</summary>
    public string UserRuntimeDirectory { get; set; } = "/run/user/1000";
}
