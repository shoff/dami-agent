using Dami.Contracts.Approvals;

namespace Dami.Core.Approvals;

/// <summary>Dispatches an approved operation to exactly one matching extension.</summary>
public sealed class ApprovalExecutionDispatcher
{
    private readonly IApprovalExecutionHandler[] handlers;

    /// <summary>Creates the open/closed approval dispatcher.</summary>
    public ApprovalExecutionDispatcher(IEnumerable<IApprovalExecutionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        this.handlers = handlers.ToArray();
    }

    /// <summary>Executes one matching handler, or returns null when none owns the request.</summary>
    public async Task<string?> ExecuteAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        IApprovalExecutionHandler? selected = null;
        foreach (var handler in this.handlers)
        {
            if (!handler.CanExecute(approval))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new InvalidOperationException(
                    $"Multiple execution handlers match approval '{approval.ApprovalId}'.");
            }

            selected = handler;
        }

        return selected is null
            ? null
            : await selected.ExecuteAsync(approval, cancellationToken).ConfigureAwait(false);
    }
}
