using Dami.Contracts.Approvals;
using Dami.Proactive.Librarian;

namespace Dami.Gateway.Cli;

/// <summary>Approvals from the shell — the CLI half of the one approval contract.</summary>
public sealed class ApprovalCommands
{
    private readonly IApprovalService approvalService;
    private readonly ManifestExecutor manifestExecutor;
    private readonly Dami.Core.Frontier.BriefExecutor briefExecutor;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public ApprovalCommands(
        IApprovalService approvalService,
        ManifestExecutor manifestExecutor,
        Dami.Core.Frontier.BriefExecutor briefExecutor,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(manifestExecutor);
        ArgumentNullException.ThrowIfNull(briefExecutor);
        ArgumentNullException.ThrowIfNull(clock);

        this.approvalService = approvalService;
        this.manifestExecutor = manifestExecutor;
        this.briefExecutor = briefExecutor;
        this.clock = clock;
    }

    /// <summary>Lists pending approvals.</summary>
    public async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var any = false;
        await foreach (var request in this.approvalService.PendingAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            any = true;
            Console.WriteLine(
                $"{request.ApprovalId.ToString("N")[..8]}  [{request.Scope}] {request.Action}");
            Console.WriteLine($"          resource: {request.Resource}");
        }

        if (!any)
        {
            Console.WriteLine("nothing awaits approval");
        }

        return 0;
    }

    /// <summary>Approves a request and, for librarian manifests, executes it.</summary>
    public async Task<int> ApproveAsync(string idPrefix, CancellationToken cancellationToken)
    {
        var request = await this.ResolvePendingAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            await Console.Error.WriteLineAsync($"no pending approval matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        await this.approvalService.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Approved, "approved via CLI",
            this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"approved: {request.Action}");

        if (request.RequestedBy == "media-librarian")
        {
            var (moved, skipped) = await this.manifestExecutor
                .ExecuteAsync(request.ApprovalId, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"executed: {moved} moved, {skipped} skipped");
        }

        if (request.RequestedBy == "frontier-brief")
        {
            Console.WriteLine("sending the approved brief to the frontier...");
            var answer = await this.briefExecutor
                .ExecuteAsync(request.ApprovalId, cancellationToken).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine(answer);
        }

        return 0;
    }

    /// <summary>Denies a request. It never runs.</summary>
    public async Task<int> DenyAsync(string idPrefix, string? note, CancellationToken cancellationToken)
    {
        var request = await this.ResolvePendingAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            await Console.Error.WriteLineAsync($"no pending approval matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        await this.approvalService.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Denied, note ?? "denied via CLI",
            this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"denied: {request.Action}");
        return 0;
    }

    private async Task<ApprovalRequest?> ResolvePendingAsync(
        string idPrefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        var normalized = idPrefix.Replace("-", "", StringComparison.Ordinal);

        await foreach (var request in this.approvalService.PendingAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (request.ApprovalId.ToString("N").StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return request;
            }
        }

        return null;
    }
}
