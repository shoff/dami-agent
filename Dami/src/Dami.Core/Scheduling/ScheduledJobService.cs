using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>Validates inert proposals and activates only explicitly confirmed jobs.</summary>
public sealed class ScheduledJobService
{
    private readonly IScheduledJobStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the scheduling application service.</summary>
    public ScheduledJobService(IScheduledJobStore store, TimeProvider timeProvider)
    {
        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Stores a validated proposal without making it runnable.</summary>
    public Task<ScheduledJob> CreateDraftAsync(
        ScheduledJobProposal proposal,
        CancellationToken cancellationToken)
    {
        Validate(proposal);
        var job = new ScheduledJob(
            Guid.NewGuid(), proposal.Name.Trim(), proposal.Description.Trim(), proposal.Kind,
            proposal.Payload.Trim(), proposal.Arguments.ToArray(), proposal.CronExpression.Trim(),
            proposal.TimeZoneId, ScheduledJobStatus.Draft, this.timeProvider.GetUtcNow(),
            null, null, null, null);
        return this.store.AddAsync(job, cancellationToken);
    }

    /// <summary>Activates the exact persisted draft and calculates its first run.</summary>
    public async Task<ScheduledJob> ConfirmAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var draft = await this.store.FindAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Scheduled job {jobId} does not exist.");
        if (draft.Status != ScheduledJobStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft job can be confirmed.");
        }

        var now = this.timeProvider.GetUtcNow();
        var next = CronSchedule.Parse(draft.CronExpression)
            .Next(now, TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId));
        return await this.store.UpdateAsync(
            draft with
            {
                Status = ScheduledJobStatus.Active,
                ConfirmedAt = now,
                NextRunAt = next,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(ScheduledJobProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (string.IsNullOrWhiteSpace(proposal.Name) || string.IsNullOrWhiteSpace(proposal.Payload))
        {
            throw new ArgumentException("A job requires a name and payload.", nameof(proposal));
        }

        _ = CronSchedule.Parse(proposal.CronExpression);
        _ = TimeZoneInfo.FindSystemTimeZoneById(proposal.TimeZoneId);
        if (proposal.Kind == ScheduledJobKind.Command && !Path.IsPathFullyQualified(proposal.Payload))
        {
            throw new ArgumentException("A command job requires an absolute executable path.", nameof(proposal));
        }
    }
}
