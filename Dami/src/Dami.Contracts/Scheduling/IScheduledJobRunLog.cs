namespace Dami.Contracts.Scheduling;

/// <summary>What one run of a scheduled job said.</summary>
/// <param name="RunId">Identity.</param>
/// <param name="JobId">The job.</param>
/// <param name="RanAt">When it ran.</param>
/// <param name="Output">What it answered.</param>
/// <param name="Delivered">False when a change-only job found nothing new and stayed silent.</param>
/// <param name="Fingerprint">For a job that watches a page, the hash of the page's text on this run.</param>
public sealed record ScheduledJobRun(
    Guid RunId, Guid JobId, DateTimeOffset RanAt, string Output, bool Delivered, string? Fingerprint = null);

/// <summary>The outputs of scheduled jobs, so a job can see what it already said.</summary>
public interface IScheduledJobRunLog
{
    /// <summary>Records one run.</summary>
    Task RecordAsync(ScheduledJobRun run, CancellationToken cancellationToken);

    /// <summary>The job's latest runs that said something, newest first.</summary>
    Task<IReadOnlyList<ScheduledJobRun>> RecentAsync(Guid jobId, int limit, CancellationToken cancellationToken);

    /// <summary>The fingerprint of the job's latest run that has one, delivered or not; null if none.</summary>
    Task<string?> LastFingerprintAsync(Guid jobId, CancellationToken cancellationToken);
}
