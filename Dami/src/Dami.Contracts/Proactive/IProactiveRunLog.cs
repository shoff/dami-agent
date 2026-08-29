namespace Dami.Contracts.Proactive;

/// <summary>The scheduler's durable memory of when each service last ran.</summary>
public interface IProactiveRunLog
{
    /// <summary>Attempts to acquire exclusive ownership of one service run.</summary>
    Task<IProactiveRunLease?> TryAcquireLeaseAsync(
        string serviceName,
        DateTimeOffset acquiredAt,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>Records that a pass ran, treating an identical record as an idempotent retry.</summary>
    /// <exception cref="InvalidOperationException">
    /// The run ID is already associated with different data.
    /// </exception>
    Task RecordAsync(
        Guid runId,
        string serviceName,
        Guid traceId,
        DateTimeOffset ranAt,
        ProactiveStatus status,
        ProactiveCadence cadence,
        CancellationToken cancellationToken);

    /// <summary>When the service last ran, or null if it never has.</summary>
    /// <remarks>
    /// Failures count: a failing service is retried at its next cadence, not hammered
    /// in a loop.
    /// </remarks>
    Task<DateTimeOffset?> LastRanAtAsync(string serviceName, CancellationToken cancellationToken);
}
