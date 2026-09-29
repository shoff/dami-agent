namespace Dami.Contracts.Scheduling;

/// <summary>What one run of a scheduled job said.</summary>
/// <param name="RunId">Identity.</param>
/// <param name="JobId">The job.</param>
/// <param name="RanAt">When it ran.</param>
/// <param name="Output">What it answered.</param>
/// <param name="Delivered">False when a change-only job found nothing new and stayed silent.</param>
public sealed record ScheduledJobRun(Guid RunId, Guid JobId, DateTimeOffset RanAt, string Output, bool Delivered);

/// <summary>The outputs of scheduled jobs, so a job can see what it already said.</summary>
public interface IScheduledJobRunLog
{
    /// <summary>Records one run.</summary>
    Task RecordAsync(ScheduledJobRun run, CancellationToken cancellationToken);

    /// <summary>The job's latest runs that said something, newest first.</summary>
    Task<IReadOnlyList<ScheduledJobRun>> RecentAsync(Guid jobId, int limit, CancellationToken cancellationToken);
}
