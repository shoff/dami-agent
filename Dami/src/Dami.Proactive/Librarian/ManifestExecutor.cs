using System.Text.Json;
using Dami.Contracts.Approvals;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive.Librarian;

/// <summary>Executes an APPROVED librarian manifest — the only component that moves files.</summary>
/// <remarks>
/// The separation D-020 demanded: the librarian proposes and holds no move code; this
/// executor moves and holds no proposal code, and refuses anything whose approval is not
/// <see cref="ApprovalStatus.Approved"/>. Moves only — no overwrite (an existing target
/// skips the file), no delete anywhere in this type, and every action logged.
/// </remarks>
public sealed class ManifestExecutor : IApprovalExecutionHandler
{
    private readonly IApprovalService approvalService;
    private readonly ILogger<ManifestExecutor> logger;

    /// <summary>Creates the executor.</summary>
    public ManifestExecutor(IApprovalService approvalService, ILogger<ManifestExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(logger);

        this.approvalService = approvalService;
        this.logger = logger;
    }

    /// <inheritdoc />
    public bool CanExecute(ApprovalRequest approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return approval.RequestedBy == "media-librarian";
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var (moved, skipped) = await this.ExecuteAsync(
            approval.ApprovalId, cancellationToken).ConfigureAwait(false);
        return $"executed: {moved} moved, {skipped} skipped";
    }

    /// <summary>Executes the manifest referenced by an approved approval.</summary>
    /// <returns>(moved, skipped) counts.</returns>
    public async Task<(int Moved, int Skipped)> ExecuteAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        var approval = await this.approvalService.FindAsync(approvalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"no approval {approvalId}");

        if (approval.Status != ApprovalStatus.Approved)
        {
            throw new InvalidOperationException(
                $"approval {approvalId} is {approval.Status}; only Approved manifests execute");
        }

        var manifest = JsonSerializer.Deserialize<MediaLibrarianService.Manifest>(
            await File.ReadAllTextAsync(approval.Resource, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException($"unreadable manifest at {approval.Resource}");

        return this.Move(manifest);
    }

    private (int Moved, int Skipped) Move(MediaLibrarianService.Manifest manifest)
    {
        var moved = 0;
        var skipped = 0;

        foreach (var proposal in manifest.Proposals)
        {
            if (!File.Exists(proposal.From) || File.Exists(proposal.To))
            {
                skipped++;
                this.logger.LogWarning(
                    "Skipping {From}: source missing or target exists", proposal.From);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(proposal.To)!);
            File.Move(proposal.From, proposal.To, overwrite: false);
            moved++;
        }

        this.logger.LogInformation("Manifest executed: {Moved} moved, {Skipped} skipped", moved, skipped);
        return (moved, skipped);
    }
}
