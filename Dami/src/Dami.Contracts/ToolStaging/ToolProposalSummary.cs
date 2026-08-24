using Dami.Contracts.Events;

namespace Dami.Contracts.ToolStaging;

/// <summary>Compact review metadata for one inert staged tool proposal.</summary>
public sealed record ToolProposalSummary(
    Guid ProposalId,
    Guid CapabilityId,
    string Name,
    string ArtifactVersion,
    ToolExecutionProfile ExecutionProfile,
    ExecutionOrigin Origin,
    DateTimeOffset ProposedAt);
