using System.Diagnostics;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Creates the trusted no-shell process boundary for one sandbox unit.</summary>
public interface ISandboxCommandFactory
{
    /// <summary>Creates the command without starting it.</summary>
    ProcessStartInfo Create(
        string toolDirectory,
        SandboxMountAccess mountAccess,
        IReadOnlyList<string> command,
        string unitName);
}
