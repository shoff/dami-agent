using System.Text;
using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.FilePatches;

namespace Dami.Capabilities.Native;

/// <summary>Files an immutable approval request for a root-confined text replacement.</summary>
[NativeCapability(
    "a5107cc1-48f7-4770-9548-7c7d9126dad8",
    "propose-file-patch",
    "Propose replacing one text file beneath the configured root; never writes the target.",
    "native://propose-file-patch/schema/v1",
    "1.0.0",
    ParametersJson = """
        {"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"],"additionalProperties":false}
        """,
    Tags = new[] { "files", "write", "approval" })]
public sealed class ProposeFilePatchCapabilityHandler : INativeCapabilityHandler
{
    private static readonly Guid approvalIdNamespace =
        new("d963ac9a-3e29-4ff0-bf03-79fe5028a85f");
    private static readonly Guid proposalIdNamespace =
        new("5892c825-b4c2-47c4-a031-44f4ef1d6749");
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    private readonly TimeProvider clock;
    private readonly BoundedFileHasher fileHasher;
    private readonly int maxBytes;
    private readonly RootedPathResolver pathResolver;
    private readonly IFilePatchProposalStore proposalStore;

    /// <summary>Creates the propose-only patch handler.</summary>
    public ProposeFilePatchCapabilityHandler(
        IFilePatchProposalStore proposalStore,
        ProposeFilePatchCapabilityOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(proposalStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        this.proposalStore = proposalStore;
        this.pathResolver = new RootedPathResolver(options.RootDirectory);
        this.fileHasher = new BoundedFileHasher(options.MaxBytes);
        this.maxBytes = options.MaxBytes;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (relativePath, content) = ReadArguments(request.Invocation.Arguments);
        this.EnsureReplacementBounded(content);
        var fullPath = this.pathResolver.ResolveFileOrMissing(relativePath);
        if (Directory.Exists(fullPath))
        {
            throw new InvalidDataException("A file patch target cannot be a directory.");
        }

        relativePath = this.pathResolver.ToRelativePath(fullPath);
        var expectedHash = File.Exists(fullPath)
            ? await this.fileHasher.HashAsync(fullPath, cancellationToken).ConfigureAwait(false)
            : null;
        var proposal = CreateProposal(request, relativePath, content, expectedHash, this.clock.GetUtcNow());
        var approval = CreateApproval(proposal);
        await this.proposalStore.CreateAsync(approval, proposal, cancellationToken).ConfigureAwait(false);
        return CreateResult(proposal);
    }

    private void EnsureReplacementBounded(string content)
    {
        if (strictUtf8.GetByteCount(content) > this.maxBytes)
        {
            throw this.CreateTooLargeException("Replacement");
        }
    }

    private InvalidDataException CreateTooLargeException(string subject)
    {
        return new InvalidDataException($"{subject} exceeds the configured limit of {this.maxBytes} bytes.");
    }

    private static FilePatchProposal CreateProposal(
        CapabilityExecutionRequest request,
        string relativePath,
        string content,
        string? expectedHash,
        DateTimeOffset createdAt)
    {
        return new FilePatchProposal(
            NativeInvocationIdentity.Derive(proposalIdNamespace, request.TraceId, request.SpanId),
            NativeInvocationIdentity.Derive(approvalIdNamespace, request.TraceId, request.SpanId),
            request.TraceId,
            request.SpanId,
            relativePath,
            content, FilePatchProposal.HashOf(content), expectedHash, createdAt);
    }

    private static ApprovalRequest CreateApproval(FilePatchProposal proposal)
    {
        var action = proposal.ExpectedSha256 is null
            ? "Create file with the reviewed proposal"
            : "Replace file contents with the reviewed proposal";
        return new ApprovalRequest(
            proposal.ApprovalId,
            proposal.TraceId,
            "native:propose-file-patch",
            action,
            "filesystem",
            proposal.RelativePath,
            proposal.CreatedAt,
            origin: Dami.Contracts.Events.ExecutionOrigin.UserTurn,
            parentSpanId: proposal.SpanId);
    }

    private static CapabilityExecutionResult CreateResult(FilePatchProposal proposal)
    {
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proposal_id"] = proposal.ProposalId.ToString(),
            ["approval_id"] = proposal.ApprovalId.ToString(),
            ["path"] = proposal.RelativePath,
            ["replacement_sha256"] = proposal.ReplacementSha256,
            ["expected_sha256"] = proposal.ExpectedSha256 ?? "absent",
            ["target_mutated"] = "false",
        };
        return new CapabilityExecutionResult("File patch proposal filed for approval.", evidence);
    }

    private static (string Path, string Content) ReadArguments(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("path", out var pathProperty)
            || pathProperty.ValueKind != JsonValueKind.String
            || pathProperty.GetString() is not { Length: > 0 } path)
        {
            throw new ArgumentException(
                "Propose-file-patch arguments require a non-empty string 'path'.", nameof(arguments));
        }

        if (!arguments.TryGetProperty("content", out var contentProperty)
            || contentProperty.ValueKind != JsonValueKind.String
            || contentProperty.GetString() is not { } content)
        {
            throw new ArgumentException(
                "Propose-file-patch arguments require a string 'content'.", nameof(arguments));
        }

        return (path, content);
    }
}
