using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Publishes and journals one exact approved tool activation.</summary>
public interface IToolActivationCoordinator
{
    /// <summary>Converges one exact activation, recording its first terminal outcome.</summary>
    Task ActivateAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken);
}
