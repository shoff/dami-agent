using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>Runs the typed payload of one scheduled job.</summary>
public interface IScheduledJobActionRunner
{
    /// <summary>Runs the job once.</summary>
    Task RunAsync(ScheduledJob job, CancellationToken cancellationToken);
}

/// <summary>Finds due jobs, runs them, and advances their durable schedule.</summary>
public sealed class ScheduledJobDispatcher
{
    private readonly IScheduledJobStore store;
    private readonly IScheduledJobActionRunner runner;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the dispatcher.</summary>
    public ScheduledJobDispatcher(
        IScheduledJobStore store,
        IScheduledJobActionRunner runner,
        TimeProvider timeProvider)
    {
        this.store = store;
        this.runner = runner;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs every active job due at the current instant.</summary>
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var jobs = await this.store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var job in jobs.Where(job =>
                     job.Status == ScheduledJobStatus.Active && job.NextRunAt <= now))
        {
            await this.RunOneAsync(job, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunOneAsync(
        ScheduledJob job,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string result;
        try
        {
            await this.runner.RunAsync(job, cancellationToken).ConfigureAwait(false);
            result = "Succeeded";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = $"Failed: {exception.Message}";
        }

        var next = CronSchedule.Parse(job.CronExpression)
            .Next(now, TimeZoneInfo.FindSystemTimeZoneById(job.TimeZoneId));
        await this.store.UpdateAsync(
            job with { LastRunAt = now, LastRunStatus = result, NextRunAt = next },
            cancellationToken).ConfigureAwait(false);
    }
}
