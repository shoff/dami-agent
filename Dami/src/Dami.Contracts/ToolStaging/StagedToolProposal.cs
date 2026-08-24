using System.Security.Cryptography;

namespace Dami.Contracts.ToolStaging;

/// <summary>An immutable version-pinned tool artifact accepted into staging.</summary>
public sealed record StagedToolProposal
{
    /// <summary>Creates one staged proposal.</summary>
    public StagedToolProposal(
        ToolProposalRequest request,
        string artifactVersion,
        DateTimeOffset proposedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVersion(artifactVersion);
        if (!string.Equals(
            request.Artifact.Version, artifactVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The artifact version does not match the staged review artifact.",
                nameof(artifactVersion));
        }

        this.Request = request;
        this.ArtifactVersion = artifactVersion;
        this.ProposedAt = proposedAt;
    }

    /// <summary>Gets the trace-owned request and exact artifact.</summary>
    public ToolProposalRequest Request { get; }

    /// <summary>Gets the lowercase SHA-256 identity of all review-relevant bytes.</summary>
    public string ArtifactVersion { get; }

    /// <summary>Gets when the proposal was accepted for staging.</summary>
    public DateTimeOffset ProposedAt { get; }

    private static void ValidateVersion(string artifactVersion)
    {
        ArgumentNullException.ThrowIfNull(artifactVersion);
        if (artifactVersion.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new ArgumentException(
                "An artifact version must be a lowercase SHA-256 value.", nameof(artifactVersion));
        }

        for (var index = 0; index < artifactVersion.Length; index++)
        {
            char character = artifactVersion[index];
            if (!char.IsAsciiDigit(character) && character is < 'a' or > 'f')
            {
                throw new ArgumentException(
                    "An artifact version must be a lowercase SHA-256 value.", nameof(artifactVersion));
            }
        }
    }
}
