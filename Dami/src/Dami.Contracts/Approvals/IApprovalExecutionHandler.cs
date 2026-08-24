namespace Dami.Contracts.Approvals;

/// <summary>One open/closed executor for approvals it explicitly recognizes.</summary>
public interface IApprovalExecutionHandler
{
    /// <summary>Reports whether this handler owns the approved operation.</summary>
    bool CanExecute(ApprovalRequest approval);

    /// <summary>Executes the operation gated by the approval.</summary>
    Task<string> ExecuteAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken);
}
