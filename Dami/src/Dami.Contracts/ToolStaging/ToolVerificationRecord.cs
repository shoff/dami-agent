using System.Text;

namespace Dami.Contracts.ToolStaging;

/// <summary>Immutable evidence that one exact staged tool produced verified bytes.</summary>
public sealed record ToolVerificationRecord
{
    /// <summary>Maximum persisted UTF-8 verification evidence.</summary>
    public const int MAX_EVIDENCE_BYTES = 65_536;

    /// <summary>The actor identity used for verification events.</summary>
    public const string VERIFIED_BY = "tools:verification";

    /// <summary>Creates exact verification evidence.</summary>
    public ToolVerificationRecord(
        Guid verificationId,
        Guid proposalId,
        string artifactVersion,
        string assemblySha256,
        string testEvidence,
        DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(artifactVersion);
        ArgumentNullException.ThrowIfNull(assemblySha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(testEvidence);
        if (verificationId == Guid.Empty || proposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tool verifications require non-empty verification and proposal identifiers.");
        }

        ToolArtifactVersion.Validate(artifactVersion, nameof(artifactVersion));
        ToolArtifactVersion.Validate(assemblySha256, nameof(assemblySha256));
        if (Encoding.UTF8.GetByteCount(testEvidence) > MAX_EVIDENCE_BYTES)
        {
            throw new ArgumentException(
                $"Verification evidence cannot exceed {MAX_EVIDENCE_BYTES} UTF-8 bytes.",
                nameof(testEvidence));
        }

        this.VerificationId = verificationId;
        this.ProposalId = proposalId;
        this.ArtifactVersion = artifactVersion;
        this.AssemblySha256 = assemblySha256;
        this.TestEvidence = testEvidence;
        this.VerifiedAt = verifiedAt;
    }

    /// <summary>Gets the retry-stable verification identifier.</summary>
    public Guid VerificationId { get; }

    /// <summary>Gets the immutable staged proposal identifier.</summary>
    public Guid ProposalId { get; }

    /// <summary>Gets the exact source/test artifact version.</summary>
    public string ArtifactVersion { get; }

    /// <summary>Gets the lowercase SHA-256 digest of the verified assembly bytes.</summary>
    public string AssemblySha256 { get; }

    /// <summary>Gets bounded evidence returned by the fixed proposal tests.</summary>
    public string TestEvidence { get; }

    /// <summary>Gets when sandboxed verification completed.</summary>
    public DateTimeOffset VerifiedAt { get; }
}
