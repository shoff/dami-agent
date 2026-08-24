namespace Dami.Contracts.Briefs;

/// <summary>Durable storage for consent-gated egress briefs.</summary>
public interface IEgressBriefStore
{
    /// <summary>Stores a new brief awaiting consent.</summary>
    Task CreateAsync(EgressBrief brief, CancellationToken cancellationToken);

    /// <summary>The brief gated by an approval, or null.</summary>
    Task<EgressBrief?> FindByApprovalAsync(Guid approvalId, CancellationToken cancellationToken);

    /// <summary>Records that the brief egressed and what came back.</summary>
    Task MarkSentAsync(
        Guid briefId,
        string answer,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken);
}
