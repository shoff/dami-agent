namespace Dami.Capabilities.Sandboxed;

/// <summary>Bounded startup-recovery result.</summary>
public readonly record struct ToolActivationRecoverySummary(
    int Found,
    int Succeeded,
    int Failed);
