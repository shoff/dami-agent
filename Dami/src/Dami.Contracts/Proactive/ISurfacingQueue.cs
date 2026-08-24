namespace Dami.Contracts.Proactive;

/// <summary>The queue Steve reads when he wants to.</summary>
/// <remarks>
/// The initial surfacing channel from architecture Phase 4 — a queue rather than a
/// notification, so proactive output waits for attention instead of demanding it.
/// Feedback capture is on the queue because the reaction is the training signal every
/// proactive service depends on (D-019).
/// </remarks>
public interface ISurfacingQueue
{
    /// <summary>
    /// Enqueues a surfacing, or suppresses it if the service is over its cap.
    /// </summary>
    /// <returns>True if the surfacing is pending; false if it was suppressed.</returns>
    /// <remarks>
    /// Suppressed surfacings are stored, not dropped. A cap that silently discards is
    /// invisible in the audit, and "how often did the cap bite" is itself a signal the
    /// thresholds need tuning.
    /// </remarks>
    Task<bool> EnqueueAsync(Surfacing surfacing, CancellationToken cancellationToken);

    /// <summary>Pending surfacings, oldest first.</summary>
    IAsyncEnumerable<Surfacing> PendingAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Marks a surfacing as delivered — Steve has seen it.</summary>
    Task DeliverAsync(Guid surfacingId, DateTimeOffset deliveredAt, CancellationToken cancellationToken);

    /// <summary>Recent surfacings in every status, newest first.</summary>
    /// <remarks>How the CLI shows history and resolves a short id to a full one.</remarks>
    IAsyncEnumerable<Surfacing> RecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Reactions Steve has recorded, newest first.</summary>
    /// <remarks>What the taste model trains on. Surfacings without feedback do not appear.</remarks>
    IAsyncEnumerable<SurfacingReaction> ReactionsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Most recent reactions to one service's surfacings, newest first.</summary>
    IAsyncEnumerable<SurfacingReaction> ReactionsForServiceAsync(
        string serviceName,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Records Steve's reaction.</summary>
    /// <exception cref="KeyNotFoundException">The surfacing does not exist.</exception>
    Task RecordFeedbackAsync(
        Guid surfacingId,
        string feedback,
        DateTimeOffset feedbackAt,
        CancellationToken cancellationToken);
}
