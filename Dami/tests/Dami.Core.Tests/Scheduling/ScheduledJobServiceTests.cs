using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Xunit;

namespace Dami.Core.Tests.Scheduling;

public sealed class ScheduledJobServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateDraftAsync_Should_Keep_A_Command_Inert_Until_Confirmation()
    {
        var store = new JobStoreStub();
        var service = new ScheduledJobService(store, new FixedTimeProvider(now));
        var proposal = new ScheduledJobProposal(
            "back up notes", "Copies notes to the archive", ScheduledJobKind.Command,
            "/usr/bin/rsync", ["-a", "/home/steve/notes/", "/mnt/archive/notes/"],
            "0 2 * * *", "America/Chicago");

        var job = await service.CreateDraftAsync(proposal, CancellationToken.None);

        Assert.Equal(ScheduledJobStatus.Draft, job.Status);
        Assert.Null(job.NextRunAt);
        Assert.Equal(proposal.Arguments, job.Arguments);
    }

    [Fact]
    public async Task ConfirmAsync_Should_Activate_The_Exact_Draft_And_Calculate_Next_Run()
    {
        var store = new JobStoreStub();
        var service = new ScheduledJobService(store, new FixedTimeProvider(now));
        var draft = await service.CreateDraftAsync(new ScheduledJobProposal(
            "morning brief", "Reviews the day", ScheduledJobKind.Prompt,
            "Review my day and brief me.", [], "0 7 * * 1-5", "America/Chicago"),
            CancellationToken.None);

        var active = await service.ConfirmAsync(draft.JobId, CancellationToken.None);

        Assert.Equal(ScheduledJobStatus.Active, active.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), active.NextRunAt);
    }

    [Fact]
    public async Task CreateDraftAsync_Should_Reject_A_Relative_Command()
    {
        var service = new ScheduledJobService(new JobStoreStub(), new FixedTimeProvider(now));
        var proposal = new ScheduledJobProposal(
            "unsafe", "", ScheduledJobKind.Command, "bash", ["-c", "echo hello"],
            "* * * * *", "UTC");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDraftAsync(proposal, CancellationToken.None));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class JobStoreStub : IScheduledJobStore
    {
        private readonly Dictionary<Guid, ScheduledJob> jobs = [];

        public Task<ScheduledJob> AddAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            this.jobs.Add(job.JobId, job);
            return Task.FromResult(job);
        }

        public Task<ScheduledJob?> FindAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(this.jobs.GetValueOrDefault(jobId));

        public Task<ScheduledJob> UpdateAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            this.jobs[job.JobId] = job;
            return Task.FromResult(job);
        }

        public Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduledJob>>(this.jobs.Values.ToList());
    }
}
