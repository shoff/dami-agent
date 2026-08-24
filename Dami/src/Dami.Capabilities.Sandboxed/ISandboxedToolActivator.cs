using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Converges one approved exact tool into the live runtime registries.</summary>
public interface ISandboxedToolActivator
{
    /// <summary>Materializes and publishes one approved tool idempotently.</summary>
    Task ActivateAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken);
}
