using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Xunit;

namespace Dami.Core.Tests.Scheduling;

public sealed class ScheduledJobDispatcherTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunDueAsync_Should_Run_Active_Due_Jobs_And_Record_The_Next_Occurrence()
    {
        var due = Job(ScheduledJobStatus.Active, now.AddMinutes(-1));
        var future = Job(ScheduledJobStatus.Active, now.AddHours(1));
        var draft = Job(ScheduledJobStatus.Draft, null);
        var store = new StoreStub(due, future, draft);
        var runner = new RunnerStub();
        var dispatcher = new ScheduledJobDispatcher(store, runner, new FixedTimeProvider(now));

        await dispatcher.RunDueAsync(CancellationToken.None);

        Assert.Equal([due.JobId], runner.Ran);
        var updated = Assert.Single(store.Updated);
        Assert.Equal("Succeeded", updated.LastRunStatus);
        Assert.Equal(now, updated.LastRunAt);
        Assert.True(updated.NextRunAt > now);
    }

    private static ScheduledJob Job(ScheduledJobStatus status, DateTimeOffset? next) => new(
        Guid.NewGuid(), "job", "description", ScheduledJobKind.Prompt, "do it", [],
        "*/15 * * * *", "UTC", status, now.AddDays(-1), now.AddDays(-1), next, null, null);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RunnerStub : IScheduledJobActionRunner
    {
        public List<Guid> Ran { get; } = [];

        public Task RunAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            this.Ran.Add(job.JobId);
            return Task.CompletedTask;
        }
    }

    private sealed class StoreStub(params ScheduledJob[] jobs) : IScheduledJobStore
    {
        public List<ScheduledJob> Updated { get; } = [];
        public Task<ScheduledJob> AddAsync(ScheduledJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ScheduledJob?> FindAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScheduledJob>>(jobs);
        public Task<ScheduledJob> UpdateAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            this.Updated.Add(job);
            return Task.FromResult(job);
        }
    }
}
