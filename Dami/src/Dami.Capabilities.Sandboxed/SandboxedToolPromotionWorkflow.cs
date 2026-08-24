using Dami.Contracts.Approvals;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Coordinates exact verification and single-resolution promotion requests.</summary>
public sealed class SandboxedToolPromotionWorkflow : IToolPromotionWorkflow
{
    private readonly TimeProvider clock;
    private readonly IToolPromotionStore promotions;
    private readonly IToolProposalStore proposals;
    private readonly string scratchRoot;
    private readonly IToolVerificationStore verifications;
    private readonly IToolArtifactVerifier verifier;

    /// <summary>Creates the exact-version promotion workflow.</summary>
    public SandboxedToolPromotionWorkflow(
        string scratchRoot,
        IToolProposalStore proposals,
        IToolVerificationStore verifications,
        IToolPromotionStore promotions,
        IToolArtifactVerifier verifier,
        TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(verifications);
        ArgumentNullException.ThrowIfNull(promotions);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(clock);
        this.scratchRoot = Path.GetFullPath(scratchRoot);
        this.proposals = proposals;
        this.verifications = verifications;
        this.promotions = promotions;
        this.verifier = verifier;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<ToolVerificationRecord> VerifyAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        StagedToolProposal proposal = await this.FindExactAsync(
            proposalId, artifactVersion, cancellationToken).ConfigureAwait(false);
        ToolVerificationRecord? existing = await this.verifications.FindAsync(
            proposalId, artifactVersion, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return await this.VerifyNewAsync(proposal, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ToolPromotionRequest> RequestPromotionAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        StagedToolProposal proposal = await this.FindExactAsync(
            proposalId, artifactVersion, cancellationToken).ConfigureAwait(false);
        ToolVerificationRecord? verification = await this.verifications.FindAsync(
            proposalId, artifactVersion, cancellationToken).ConfigureAwait(false);
        if (verification is null)
        {
            throw new InvalidOperationException(
                "An exact successful verification is required before promotion.");
        }

        return await this.RequestNewOrExistingAsync(proposal, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ToolVerificationRecord> VerifyNewAsync(
        StagedToolProposal proposal,
        CancellationToken cancellationToken)
    {
        string scratch = Path.Combine(this.scratchRoot, $".dami-promote-{Guid.NewGuid():N}");
        try
        {
            VerifiedToolArtifact artifact = await this.verifier.VerifyAsync(
                proposal.Request.Artifact, scratch, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                artifact.ArtifactVersion, proposal.ArtifactVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Verification returned a different artifact version.");
            }

            var record = new ToolVerificationRecord(
                ToolPromotionIdentity.Derive(
                    proposal.Request.ProposalId, proposal.ArtifactVersion, discriminator: 1),
                proposal.Request.ProposalId, proposal.ArtifactVersion, artifact.AssemblySha256,
                artifact.TestEvidence, this.clock.GetUtcNow());
            return await this.verifications.RecordAsync(record, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
    }

    private async Task<ToolPromotionRequest> RequestNewOrExistingAsync(
        StagedToolProposal proposal,
        CancellationToken cancellationToken)
    {
        Guid approvalId = ToolPromotionIdentity.Derive(
            proposal.Request.ProposalId, proposal.ArtifactVersion, discriminator: 2);
        ToolPromotionRequest? existing = await this.promotions.FindByApprovalAsync(
            approvalId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureExact(existing, proposal);
            return existing;
        }

        ToolPromotionRequest request = CreateRequest(proposal, approvalId, this.clock.GetUtcNow());
        return await this.promotions.RequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StagedToolProposal> FindExactAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactVersion);
        StagedToolProposal proposal = await this.proposals.FindAsync(
            proposalId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tool proposal '{proposalId}' was not found.");
        if (!string.Equals(
            proposal.ArtifactVersion, artifactVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The reviewed artifact version does not match.");
        }

        return proposal;
    }

    private static ToolPromotionRequest CreateRequest(
        StagedToolProposal proposal,
        Guid approvalId,
        DateTimeOffset requestedAt)
    {
        Guid proposalId = proposal.Request.ProposalId;
        string version = proposal.ArtifactVersion;
        var approval = new ApprovalRequest(
            approvalId, proposal.Request.TraceId, ToolPromotionRequest.REQUESTED_BY,
            $"promote sandboxed tool {proposal.Request.Artifact.Schema.Name}",
            ToolPromotionRequest.SCOPE, ToolPromotionRequest.Resource(proposalId, version),
            requestedAt, origin: proposal.Request.Origin, parentSpanId: proposal.Request.SpanId);
        return new ToolPromotionRequest(
            ToolPromotionIdentity.Derive(proposalId, version, discriminator: 3),
            proposalId, version, approval);
    }

    private static void EnsureExact(
        ToolPromotionRequest promotion,
        StagedToolProposal proposal)
    {
        if (promotion.ProposalId != proposal.Request.ProposalId
            || !string.Equals(
                promotion.ArtifactVersion, proposal.ArtifactVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stored promotion targets a different artifact.");
        }
    }
}
