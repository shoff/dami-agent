using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Materializes verified source into an exact runtime artifact directory.</summary>
public interface ISandboxedToolMaterializer
{
    /// <summary>Idempotently installs one exact verified proposal.</summary>
    Task<SandboxedCapabilityRegistration> MaterializeAsync(
        Guid promotionId,
        StagedToolProposal proposal,
        ToolVerificationRecord verification,
        CancellationToken cancellationToken);
}
