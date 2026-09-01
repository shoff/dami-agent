namespace Dami.Contracts.Scheduling;

/// <summary>Persists scheduled jobs independently of their execution mechanism.</summary>
public interface IScheduledJobStore
{
    /// <summary>Adds a new job.</summary>
    Task<ScheduledJob> AddAsync(ScheduledJob job, CancellationToken cancellationToken);

    /// <summary>Finds one job by identifier.</summary>
    Task<ScheduledJob?> FindAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Updates one existing job.</summary>
    Task<ScheduledJob> UpdateAsync(ScheduledJob job, CancellationToken cancellationToken);

    /// <summary>Lists jobs for the dashboard.</summary>
    Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken);
}
