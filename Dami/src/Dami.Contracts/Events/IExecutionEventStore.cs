
namespace Dami.Contracts.Events;

/// <summary>Append-only, replayable storage for the execution stream.</summary>
/// <remarks>
/// There is deliberately no update and no delete. The store is the audit record, and the
/// database enforces that independently — the runtime role holds no UPDATE or DELETE
/// privilege, and a trigger refuses both even for the owner. This interface simply does
/// not offer the operation.
/// </remarks>
public interface IExecutionEventStore
{
    /// <summary>Appends one event, returning the sequence the store assigned.</summary>
    /// <remarks>
    /// Idempotent on <see cref="ExecutionEvent.EventId"/>: appending an event that is
    /// already stored returns the existing sequence rather than duplicating it or
    /// throwing. Reconnect and retry therefore cannot double-write, which is what
    /// acceptance item 1 requires.
    /// </remarks>
    Task<long> AppendAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken);

    /// <summary>Replays one trace in persistence order.</summary>
    IAsyncEnumerable<ExecutionEvent> ReplayAsync(Guid traceId, CancellationToken cancellationToken);

    /// <summary>Resolves a short hex prefix to a full trace id; null if none or ambiguous.</summary>
    Task<Guid?> FindTraceByPrefixAsync(string hexPrefix, CancellationToken cancellationToken);

    /// <summary>Reads events appended after <paramref name="afterSequence"/>, oldest first.</summary>
    /// <remarks>
    /// How a reconnecting client catches up without re-reading the whole stream.
    /// </remarks>
    IAsyncEnumerable<ExecutionEvent> ReadSinceAsync(
        long afterSequence,
        int limit,
        CancellationToken cancellationToken);
}
