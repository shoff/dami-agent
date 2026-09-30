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
    private readonly Dami.Contracts.Runtime.IPauseSwitch pause;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the dispatcher.</summary>
    public ScheduledJobDispatcher(
        IScheduledJobStore store,
        IScheduledJobActionRunner runner,
        Dami.Contracts.Runtime.IPauseSwitch pause,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(pause);
        this.store = store;
        this.runner = runner;
        this.pause = pause;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs every active job due at the current instant.</summary>
    /// <remarks>
    /// While paused (A10) a due job is not run but moved to its next occurrence, so resuming
    /// does not fire the backlog the pause built up.
    /// </remarks>
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var paused = await this.pause.CurrentAsync(now, cancellationToken).ConfigureAwait(false);
        var jobs = await this.store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var job in jobs.Where(job =>
                     job.Status == ScheduledJobStatus.Active && job.NextRunAt <= now))
        {
            await (paused is null
                ? this.RunOneAsync(job, now, cancellationToken)
                : this.RecordAsync(job, now, $"Skipped: paused ({paused.Reason})", cancellationToken)).ConfigureAwait(false);
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

        await this.RecordAsync(job, now, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(ScheduledJob job, DateTimeOffset now, string result, CancellationToken cancellationToken)
    {
        var next = CronSchedule.Parse(job.CronExpression)
            .Next(now, TimeZoneInfo.FindSystemTimeZoneById(job.TimeZoneId));
        await this.store.UpdateAsync(
            job with { LastRunAt = now, LastRunStatus = result, NextRunAt = next },
            cancellationToken).ConfigureAwait(false);
    }
}
