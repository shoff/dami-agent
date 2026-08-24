namespace Dami.Capabilities.Sandboxed;

/// <summary>Whether the isolated process can mutate its sole host-backed mount.</summary>
public enum SandboxMountAccess
{
    /// <summary>The verified runtime artifact is visible but immutable.</summary>
    ReadOnly = 0,

    /// <summary>A disposable verification workspace may receive compiler output.</summary>
    WritableScratch = 1,
}
