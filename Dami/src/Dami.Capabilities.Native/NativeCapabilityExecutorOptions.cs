namespace Dami.Capabilities.Native;

/// <summary>Safety bounds for in-process capability execution.</summary>
public sealed class NativeCapabilityExecutorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "NativeCapabilityExecutor";

    /// <summary>Gets or sets the maximum duration of one native invocation.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
