using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Builds and tests one exact proposal in the fixed sandbox envelope.</summary>
public interface IToolArtifactVerifier
{
    /// <summary>Returns verified derived bytes in caller-owned scratch space.</summary>
    Task<VerifiedToolArtifact> VerifyAsync(
        ToolProposalArtifact artifact,
        string scratchDirectory,
        CancellationToken cancellationToken);
}
