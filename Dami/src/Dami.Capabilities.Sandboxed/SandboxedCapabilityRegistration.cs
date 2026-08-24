using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Exact verified bytes available to the sandboxed execution source.</summary>
public sealed class SandboxedCapabilityRegistration
{
    /// <summary>Creates one immutable executable registration.</summary>
    public SandboxedCapabilityRegistration(
        Guid capabilityId,
        ToolVerificationRecord verification,
        string artifactDirectory)
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "A sandboxed capability requires a stable identifier.", nameof(capabilityId));
        }

        ArgumentNullException.ThrowIfNull(verification);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        this.CapabilityId = capabilityId;
        this.Verification = verification;
        this.ArtifactDirectory = Path.GetFullPath(artifactDirectory);
    }

    /// <summary>Gets the stable capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the exact verification that pins source and executable bytes.</summary>
    public ToolVerificationRecord Verification { get; }

    /// <summary>Gets the host path mounted read-only as <c>/tool</c>.</summary>
    public string ArtifactDirectory { get; }

    /// <summary>Gets the exact staged source/test version.</summary>
    public string ArtifactVersion => this.Verification.ArtifactVersion;

    /// <summary>Gets the exact verified assembly digest.</summary>
    public string AssemblySha256 => this.Verification.AssemblySha256;
}
