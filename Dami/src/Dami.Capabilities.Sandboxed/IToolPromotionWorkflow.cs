using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Verifies and requests human promotion of one exact staged tool version.</summary>
public interface IToolPromotionWorkflow
{
    /// <summary>Builds/tests and records exact durable verification evidence.</summary>
    Task<ToolVerificationRecord> VerifyAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken);

    /// <summary>Creates the single-resolution approval for one verified exact version.</summary>
    Task<ToolPromotionRequest> RequestPromotionAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken);
}
