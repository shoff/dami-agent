namespace Dami.Contracts.ToolStaging;

/// <summary>Immutable terminal outcome for one exact tool-publication attempt.</summary>
public sealed record ToolActivationOutcome
{
    /// <summary>Maximum persisted failure-code length.</summary>
    public const int MAX_FAILURE_CODE_LENGTH = 128;

    /// <summary>The actor identity used for activation events.</summary>
    public const string ACTIVATED_BY = "tools:activation";

    /// <summary>Creates one terminal activation outcome.</summary>
    public ToolActivationOutcome(
        Guid activationId,
        Guid promotionId,
        Guid verificationId,
        ToolActivationStatus status,
        string? failureCode,
        DateTimeOffset occurredAt)
    {
        if (activationId == Guid.Empty || promotionId == Guid.Empty || verificationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tool activation outcomes require non-empty activation, promotion, and verification identifiers.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        bool hasFailure = !string.IsNullOrWhiteSpace(failureCode);
        if ((status == ToolActivationStatus.Failed) != hasFailure
            || failureCode?.Length > MAX_FAILURE_CODE_LENGTH)
        {
            throw new ArgumentException(
                "Failed activations require a bounded failure code; successful activations cannot have one.",
                nameof(failureCode));
        }

        this.ActivationId = activationId;
        this.PromotionId = promotionId;
        this.VerificationId = verificationId;
        this.Status = status;
        this.FailureCode = failureCode;
        this.OccurredAt = occurredAt;
    }

    /// <summary>Gets the retry-stable activation-attempt identifier.</summary>
    public Guid ActivationId { get; }

    /// <summary>Gets the human-approved promotion identifier.</summary>
    public Guid PromotionId { get; }

    /// <summary>Gets the exact successful verification identifier.</summary>
    public Guid VerificationId { get; }

    /// <summary>Gets the terminal publication status.</summary>
    public ToolActivationStatus Status { get; }

    /// <summary>Gets the bounded non-sensitive failure classification.</summary>
    public string? FailureCode { get; }

    /// <summary>Gets when publication reached this terminal outcome.</summary>
    public DateTimeOffset OccurredAt { get; }
}
