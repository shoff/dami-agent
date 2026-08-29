namespace Dami.Contracts.Proactive;

/// <summary>One recorded pass of one proactive service.</summary>
public sealed record ProactiveRun(
    Guid RunId,
    Guid TraceId,
    DateTimeOffset RanAt,
    ProactiveStatus Status);

/// <summary>What one proactive service has done, newest first.</summary>
public sealed record ProactiveServiceHistory(
    string ServiceName,
    int Runs,
    DateTimeOffset LastRanAt,
    ProactiveStatus LastStatus,
    IReadOnlyList<ProactiveRun> Recent);

/// <summary>Reads back what the proactive tier has been doing.</summary>
/// <remarks>
/// Separate from <see cref="IProactiveRunLog"/> on purpose: that one is the scheduler's
/// write side — lease, record, last-ran — and the scheduler has no use for a query. This
/// is the read side, for anything that wants to show a person what ran and when.
///
/// It exists because there was no way to see it. The tier runs unattended on an hourly
/// tick and writes to <c>dami.proactive_runs</c>, but nothing surfaced that: three of the
/// eleven services had not run since 2026-08-23 and it was impossible to tell from any
/// interface whether that was their cadence or a fault.
/// </remarks>
public interface IProactiveRunHistory
{
    /// <summary>
    /// Every service that has ever run, most recently active first, each carrying up to
    /// <paramref name="recentPerService"/> of its latest passes.
    /// </summary>
    Task<IReadOnlyList<ProactiveServiceHistory>> ReadAsync(
        int recentPerService,
        CancellationToken cancellationToken);
}
