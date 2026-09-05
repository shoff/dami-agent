using System.Text.Json;
using Dami.Contracts.Scheduling;
using Dami.Core.Frontier;
using Dami.Core.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class ScheduleToolsTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);

    private readonly MemoryStore store = new();

    private ScheduleTools Subject() => new(
        new ScheduledJobService(this.store, new FixedClock(now)), this.store, NullLogger<ScheduleTools>.Instance);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string PORTRAIT = """
        {"name":"morning portrait","description":"a picture each morning","request":"make a picture of yourself starting the day","cron":"0 7 * * *","timeZoneId":"America/Chicago"}
        """;

    [Fact]
    public async Task Schedule_Should_Create_A_Draft_Delivered_To_The_Channel_And_Not_Activate_It()
    {
        var result = await this.Subject().ScheduleAsync("discord:1", Arguments(PORTRAIT), CancellationToken.None);

        Assert.True(result.Success);
        var draft = Assert.Single(this.store.Jobs);
        Assert.Equal((ScheduledJobStatus.Draft, ScheduledJobKind.Prompt, "discord:1"), (draft.Status, draft.Kind, draft.Delivery));
        Assert.Null(draft.NextRunAt);
        Assert.Contains(draft.JobId.ToString("N")[..8], result.Text, StringComparison.Ordinal);
        Assert.Contains("not active", result.Text, StringComparison.Ordinal);
        Assert.Contains("confirm_schedule", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_Should_Activate_The_Draft_By_Its_Short_Id()
    {
        var tools = this.Subject();
        await tools.ScheduleAsync("discord:1", Arguments(PORTRAIT), CancellationToken.None);
        var draft = Assert.Single(this.store.Jobs);

        var result = await tools.ConfirmAsync(draft.JobId.ToString("N")[..8].ToUpperInvariant(), CancellationToken.None);

        Assert.True(result.Success);
        var active = Assert.Single(this.store.Jobs);
        Assert.Equal(ScheduledJobStatus.Active, active.Status);
        Assert.True(active.NextRunAt > now);
        Assert.Contains("Active", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_Should_Fail_In_Words_For_An_Unknown_Id()
    {
        var result = await this.Subject().ConfirmAsync("deadbeef", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("deadbeef", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_Should_Not_Activate_Something_Already_Active()
    {
        var tools = this.Subject();
        await tools.ScheduleAsync("gui", Arguments(PORTRAIT), CancellationToken.None);
        var id = this.store.Jobs[0].JobId.ToString("N")[..8];
        await tools.ConfirmAsync(id, CancellationToken.None);

        var again = await tools.ConfirmAsync(id, CancellationToken.None);

        Assert.False(again.Success);
    }

    [Fact]
    public async Task Schedule_Should_Reject_A_Bad_Cron_Before_Writing()
    {
        var bad = Arguments("""{"name":"x","description":"y","request":"z","cron":"every day","timeZoneId":"UTC"}""");

        await Assert.ThrowsAsync<FormatException>(
            () => this.Subject().ScheduleAsync("gui", bad, CancellationToken.None));

        Assert.Empty(this.store.Jobs);
    }

    [Fact]
    public async Task Schedule_Should_Require_Every_Argument()
    {
        var missing = Arguments("""{"name":"x","description":"y","cron":"0 7 * * *","timeZoneId":"UTC"}""");

        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().ScheduleAsync("gui", missing, CancellationToken.None));
    }

    private sealed class FixedClock(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class MemoryStore : IScheduledJobStore
    {
        public List<ScheduledJob> Jobs { get; } = [];

        public Task<ScheduledJob> AddAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            this.Jobs.Add(job);
            return Task.FromResult(job);
        }

        public Task<ScheduledJob?> FindAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(this.Jobs.FirstOrDefault(job => job.JobId == jobId));

        public Task<ScheduledJob> UpdateAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            var index = this.Jobs.FindIndex(item => item.JobId == job.JobId);
            this.Jobs[index] = job;
            return Task.FromResult(job);
        }

        public Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduledJob>>(this.Jobs.ToList());
    }
}
