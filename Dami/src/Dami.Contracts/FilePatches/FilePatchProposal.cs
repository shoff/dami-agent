using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Dami.Contracts.FilePatches;

/// <summary>Exact replacement bytes and preimage identity held behind one approval.</summary>
public sealed record FilePatchProposal
{
    /// <summary>Creates an immutable, hash-pinned file patch proposal.</summary>
    public FilePatchProposal(
        Guid proposalId,
        Guid approvalId,
        Guid traceId,
        Guid spanId,
        string relativePath,
        string replacementContent,
        string replacementSha256,
        string? expectedSha256,
        DateTimeOffset createdAt)
    {
        ValidateId(proposalId, nameof(proposalId));
        ValidateId(approvalId, nameof(approvalId));
        ValidateId(traceId, nameof(traceId));
        ValidateId(spanId, nameof(spanId));
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(replacementContent);
        if (replacementContent.AsSpan().Contains('\0'))
        {
            throw new ArgumentException(
                "Replacement text cannot contain NUL characters.", nameof(replacementContent));
        }

        ValidateHash(replacementSha256, nameof(replacementSha256));
        if (!string.Equals(HashOf(replacementContent), replacementSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Replacement content does not match its SHA-256.", nameof(replacementSha256));
        }

        if (expectedSha256 is not null)
        {
            ValidateHash(expectedSha256, nameof(expectedSha256));
        }

        this.ProposalId = proposalId;
        this.ApprovalId = approvalId;
        this.TraceId = traceId;
        this.SpanId = spanId;
        this.RelativePath = relativePath;
        this.ReplacementContent = replacementContent;
        this.ReplacementSha256 = replacementSha256;
        this.ExpectedSha256 = expectedSha256;
        this.CreatedAt = createdAt;
    }

    /// <summary>Gets the proposal identifier.</summary>
    public Guid ProposalId { get; }

    /// <summary>Gets the approval that gates this proposal.</summary>
    public Guid ApprovalId { get; }

    /// <summary>Gets the originating execution trace.</summary>
    public Guid TraceId { get; }

    /// <summary>Gets the originating tool span.</summary>
    public Guid SpanId { get; }

    /// <summary>Gets the workspace-root-relative target path.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the exact replacement text reviewed for approval.</summary>
    public string ReplacementContent { get; }

    /// <summary>Gets the SHA-256 of the UTF-8 replacement bytes.</summary>
    public string ReplacementSha256 { get; }

    /// <summary>Gets the required current-content SHA-256, or null for create-only.</summary>
    public string? ExpectedSha256 { get; }

    /// <summary>Gets when the proposal was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Hashes UTF-8 content without allocating a full-size byte array.</summary>
    public static string HashOf(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var byteCount = Encoding.UTF8.GetByteCount(content);
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        try
        {
            var written = Encoding.UTF8.GetBytes(content.AsSpan(), bytes);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(bytes.AsSpan(0, written), hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("File patch identifiers cannot be empty.", parameterName);
        }
    }

    private static void ValidateHash(string hash, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(hash, parameterName);
        if (hash.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new ArgumentException("SHA-256 values must contain 64 hexadecimal characters.", parameterName);
        }

        foreach (var character in hash)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                throw new ArgumentException(
                    "SHA-256 values must contain only hexadecimal characters.", parameterName);
            }
        }
    }
}
