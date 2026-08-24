using System.Text;
using Dami.Contracts.Approvals;
using Dami.Contracts.FilePatches;

namespace Dami.Capabilities.Native;

/// <summary>Applies byte-exact file proposals only after durable approval.</summary>
public sealed class FilePatchExecutor : IApprovalExecutionHandler
{
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    private readonly IApprovalService approvalService;
    private readonly BoundedFileHasher fileHasher;
    private readonly int maxBytes;
    private readonly RootedPathResolver pathResolver;
    private readonly IFilePatchProposalStore proposalStore;

    /// <summary>Creates the approval-gated executor.</summary>
    public FilePatchExecutor(
        IApprovalService approvalService,
        IFilePatchProposalStore proposalStore,
        ProposeFilePatchCapabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(proposalStore);
        ArgumentNullException.ThrowIfNull(options);
        this.approvalService = approvalService;
        this.proposalStore = proposalStore;
        this.pathResolver = new RootedPathResolver(options.RootDirectory);
        this.fileHasher = new BoundedFileHasher(options.MaxBytes);
        this.maxBytes = options.MaxBytes;
    }

    /// <inheritdoc />
    public bool CanExecute(ApprovalRequest approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return approval.RequestedBy == "native:propose-file-patch";
    }

    /// <inheritdoc />
    public Task<string> ExecuteAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return this.ExecuteAsync(approval.ApprovalId, cancellationToken);
    }

    /// <summary>Applies the proposal attached to one approved request.</summary>
    public async Task<string> ExecuteAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        var approval = await this.approvalService.FindAsync(approvalId, cancellationToken)
            .ConfigureAwait(false);
        if (approval is null || approval.Status != ApprovalStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Approval {approvalId} is not Approved; the file patch was not applied.");
        }

        var proposal = await this.proposalStore.FindByApprovalAsync(approvalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No file patch proposal is attached to {approvalId}.");
        ValidateHandoff(approval, proposal);
        this.EnsureReplacementBounded(proposal.ReplacementContent);
        var fullPath = this.pathResolver.ResolveFileOrMissing(proposal.RelativePath);
        if (!string.Equals(
            this.pathResolver.ToRelativePath(fullPath), proposal.RelativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approved patch path is not canonical.");
        }

        return proposal.ExpectedSha256 is null
            ? await this.CreateAsync(fullPath, proposal, cancellationToken).ConfigureAwait(false)
            : await this.ReplaceExistingAsync(fullPath, proposal, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureReplacementBounded(string replacement)
    {
        if (strictUtf8.GetByteCount(replacement) > this.maxBytes)
        {
            throw new InvalidDataException(
                $"Replacement exceeds the configured limit of {this.maxBytes} UTF-8 bytes.");
        }
    }

    private async Task<string> CreateAsync(
        string fullPath,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"File '{proposal.RelativePath}' appeared after review; create was refused.");
        }

        if (File.Exists(fullPath))
        {
            var currentHash = await this.fileHasher.HashAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(
                currentHash, proposal.ReplacementSha256, StringComparison.OrdinalIgnoreCase))
            {
                return $"already applied: {proposal.RelativePath}";
            }

            throw new InvalidOperationException(
                $"File '{proposal.RelativePath}' appeared after review; create was refused.");
        }

        await this.WriteAndMoveAsync(
            fullPath, proposal, overwrite: false, cancellationToken).ConfigureAwait(false);
        return $"executed: created {proposal.RelativePath}";
    }

    private async Task<string> ReplaceExistingAsync(
        string fullPath,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        var currentHash = await this.fileHasher.HashAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(currentHash, proposal.ReplacementSha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"already applied: {proposal.RelativePath}";
        }

        if (!string.Equals(currentHash, proposal.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"File '{proposal.RelativePath}' changed after review; the patch was not applied.");
        }

        var moved = await this.WriteAndMoveAsync(
            fullPath, proposal, overwrite: true, cancellationToken).ConfigureAwait(false);
        return moved
            ? $"executed: replaced {proposal.RelativePath}"
            : $"already applied: {proposal.RelativePath}";
    }

    private static void ValidateHandoff(ApprovalRequest approval, FilePatchProposal proposal)
    {
        if (approval.RequestedBy != "native:propose-file-patch"
            || approval.ApprovalId != proposal.ApprovalId
            || approval.TraceId != proposal.TraceId
            || !string.Equals(approval.Resource, proposal.RelativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approved request does not match its file patch proposal.");
        }
    }

    private async Task<bool> WriteAndMoveAsync(
        string fullPath,
        FilePatchProposal proposal,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The patch target has no parent directory.");
        var temporary = Path.Combine(directory, $".dami-patch-{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteTemporaryAsync(
                temporary, proposal.ReplacementContent, cancellationToken).ConfigureAwait(false);
            if (overwrite
                && !await this.TargetStillExpectedAsync(
                    fullPath, proposal, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (overwrite && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(fullPath));
            }

            File.Move(temporary, fullPath, overwrite);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<bool> TargetStillExpectedAsync(
        string fullPath,
        FilePatchProposal proposal,
        CancellationToken cancellationToken)
    {
        var latestHash = await this.fileHasher.HashAsync(
            fullPath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(
            latestHash, proposal.ReplacementSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
            latestHash, proposal.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"File '{proposal.RelativePath}' changed while preparing the patch.");
        }

        return true;
    }

    private static async Task WriteTemporaryAsync(
        string temporary,
        string content,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(temporary, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            Share = FileShare.None,
        });
        await using var writer = new StreamWriter(stream, strictUtf8, leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
