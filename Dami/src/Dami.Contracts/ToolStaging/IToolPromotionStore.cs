namespace Dami.Contracts.ToolStaging;

/// <summary>Persists exact-version tool promotion requests with their human approval.</summary>
public interface IToolPromotionStore
{
    /// <summary>Requests promotion idempotently in one transaction.</summary>
    Task<ToolPromotionRequest> RequestAsync(
        ToolPromotionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Finds the promotion owned by one approval.</summary>
    Task<ToolPromotionRequest?> FindByApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken);
}
