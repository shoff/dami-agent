using System.Text;

namespace Dami.Contracts.Capabilities;

/// <summary>An immutable skill change and its exact durable textual diff.</summary>
public sealed record SkillChangeRecord
{
    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Creates a durable skill change record.</summary>
    public SkillChangeRecord(
        SkillChangeRequest request,
        string diff,
        string? replacementVersion,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDiff(diff);

        bool requiresReplacement = request.Kind is SkillChangeKind.Author or SkillChangeKind.Revise;
        if (requiresReplacement != !string.IsNullOrWhiteSpace(replacementVersion))
        {
            throw new ArgumentException(
                "Author and revise require a replacement version; retire forbids one.",
                nameof(replacementVersion));
        }

        if (replacementVersion is not null && !SkillVersion.IsCanonical(replacementVersion))
        {
            throw new ArgumentException(
                "A replacement version must be a lowercase SHA-256 value.",
                nameof(replacementVersion));
        }

        this.Request = request;
        this.Diff = diff;
        this.ReplacementVersion = replacementVersion;
        this.RequestedAt = requestedAt;
    }

    /// <summary>Gets the version-pinned lifecycle request.</summary>
    public SkillChangeRequest Request { get; }

    /// <summary>Gets the exact bounded textual diff.</summary>
    public string Diff { get; }

    /// <summary>Gets the resulting semantic version, absent for retirement.</summary>
    public string? ReplacementVersion { get; }

    /// <summary>Gets when the change was durably requested.</summary>
    public DateTimeOffset RequestedAt { get; }

    private static void ValidateDiff(string diff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diff);
        try
        {
            if (strictUtf8.GetByteCount(diff) > 1_048_576)
            {
                throw new ArgumentOutOfRangeException(nameof(diff));
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("A diff must contain valid Unicode.", nameof(diff), exception);
        }
    }
}
