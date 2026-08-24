namespace Dami.Capabilities.Sandboxed;

/// <summary>Host-enforced resource limits for one sandbox process tree.</summary>
public sealed class SandboxProcessOptions
{
    /// <summary>Gets or sets the UTF-8 standard-input ceiling.</summary>
    public int MaxInputBytes { get; init; } = 1_048_576;

    /// <summary>Gets or sets the combined standard-output and error ceiling.</summary>
    public int MaxOutputBytes { get; init; } = 1_048_576;

    /// <summary>Gets or sets the cgroup memory ceiling in bytes.</summary>
    public long MemoryMaxBytes { get; init; } = 268_435_456;

    /// <summary>Gets or sets the cgroup process/thread ceiling.</summary>
    public int ProcessMax { get; init; } = 16;

    /// <summary>Gets or sets the systemd and caller wall-clock ceiling.</summary>
    public TimeSpan RuntimeMax { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the owning user's runtime directory containing its bus.</summary>
    public string UserRuntimeDirectory { get; init; } = "/run/user/1000";
}
