namespace Dami.Contracts.Capabilities;

/// <summary>Reads incomplete skill changes and records materialization outcomes.</summary>
public interface ISkillChangeRecoveryStore
{
    /// <summary>Returns whether a durable change still lacks a success event.</summary>
    Task<bool> IsPendingAsync(Guid changeId, CancellationToken cancellationToken);

    /// <summary>Returns the oldest bounded set without a durable success event.</summary>
    Task<IReadOnlyList<SkillChangeRecord>> FindPendingAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Appends an idempotent success event for a materialized change.</summary>
    Task RecordSucceededAsync(
        SkillChangeRecord record,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>Appends an idempotent, non-sensitive failure event without resolving the change.</summary>
    Task RecordFailedAsync(
        SkillChangeRecord record,
        string failureCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
