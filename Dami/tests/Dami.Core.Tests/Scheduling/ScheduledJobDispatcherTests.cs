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
        var dispatcher = new ScheduledJobDispatcher(store, runner, new PauseStub(null), new FixedTimeProvider(now));

        await dispatcher.RunDueAsync(CancellationToken.None);

        Assert.Equal([due.JobId], runner.Ran);
        var updated = Assert.Single(store.Updated);
        Assert.Equal("Succeeded", updated.LastRunStatus);
        Assert.Equal(now, updated.LastRunAt);
        Assert.True(updated.NextRunAt > now);
    }

    [Fact]
    public async Task While_Paused_A_Due_Job_Should_Not_Run_But_Should_Move_To_Its_Next_Occurrence()
    {
        // Resuming must not fire the backlog the pause built up.
        var due = Job(ScheduledJobStatus.Active, now.AddMinutes(-1));
        var store = new StoreStub(due);
        var runner = new RunnerStub();

        await new ScheduledJobDispatcher(
                store, runner, new PauseStub(new Dami.Contracts.Runtime.PauseState(null, "away", now)), new FixedTimeProvider(now))
            .RunDueAsync(CancellationToken.None);

        Assert.Empty(runner.Ran);
        var updated = Assert.Single(store.Updated);
        Assert.Equal("Skipped: paused (away)", updated.LastRunStatus);
        Assert.True(updated.NextRunAt > now);
    }

    private sealed class PauseStub(Dami.Contracts.Runtime.PauseState? state) : Dami.Contracts.Runtime.IPauseSwitch
    {
        public Task<Dami.Contracts.Runtime.PauseState?> CurrentAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(state);
        public Task PauseAsync(DateTimeOffset? until, string reason, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
