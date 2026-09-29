using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Core.Frontier;
using Dami.Core.Scheduling;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

/// <summary>
/// A Prompt job never touches the local model (ADR-0028), goes where it was asked, remembers
/// what it said, and — when asked to — says nothing when nothing changed (C1, C2).
/// </summary>
public sealed class ScheduledJobActionRunnerTests
{
    private readonly IScheduledPromptDelivery discord = Substitute.For<IScheduledPromptDelivery>();
    private readonly IAugmentedTurn augmented = Substitute.For<IAugmentedTurn>();
    private readonly ISurfacingQueue surfacings = Substitute.For<ISurfacingQueue>();
    private readonly IScheduledJobRunLog runs = Substitute.For<IScheduledJobRunLog>();

    public ScheduledJobActionRunnerTests()
    {
        this.runs.RecentAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ScheduledJobRun>());
    }

    private static ScheduledJob Job(string? delivery, bool onlyWhenNew = false) => new(
        Guid.NewGuid(), "weekly summary", "d", ScheduledJobKind.Prompt, "summarise my week", [],
        "0 8 * * 1", "America/Chicago", ScheduledJobStatus.Active, DateTimeOffset.UnixEpoch,
        null, null, null, null, delivery, onlyWhenNew);

    private ScheduledJobActionRunner Subject()
    {
        this.discord.Handles(Arg.Is<string?>(key => key != null && key.StartsWith("discord:", StringComparison.Ordinal)))
            .Returns(true);
        return new ScheduledJobActionRunner([this.discord], this.augmented, this.surfacings, this.runs, TimeProvider.System);
    }

    private void Remembers(ScheduledJob job, string said) =>
        this.runs.RecentAsync(job.JobId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ScheduledJobRun(Guid.NewGuid(), job.JobId, DateTimeOffset.UnixEpoch, said, true)]);

    [Fact]
    public async Task A_Job_With_A_Channel_Should_Be_Delivered_There_And_What_It_Said_Recorded()
    {
        var job = Job("discord:1");
        this.discord.DeliverAsync(job, Arg.Any<string>(), false, Arg.Any<CancellationToken>()).Returns("a quiet week");

        await this.Subject().RunAsync(job, CancellationToken.None);

        await this.discord.Received(1).DeliverAsync(
            job, "[scheduled job 'weekly summary'] summarise my week", false, Arg.Any<CancellationToken>());
        await this.augmented.DidNotReceive().RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.runs.Received(1).RecordAsync(
            Arg.Is<ScheduledJobRun>(run => run.JobId == job.JobId && run.Output == "a quiet week" && run.Delivered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Job_Without_A_Channel_Should_Be_Answered_By_The_Frontier_And_Surfaced()
    {
        this.augmented.RunAsync("[scheduled job 'weekly summary'] summarise my week", Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "a quiet week", 3, 200));

        await this.Subject().RunAsync(Job(null), CancellationToken.None);

        await this.surfacings.Received(1).EnqueueAsync(
            Arg.Is<Surfacing>(surfacing =>
                surfacing.ServiceName == "scheduled-job"
                && surfacing.Title == "weekly summary"
                && surfacing.Body == "a quiet week"),
            Arg.Any<CancellationToken>());
        await this.discord.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task A_Run_Should_Be_Shown_What_The_Job_Said_Last_Time()
    {
        var job = Job(null);
        this.Remembers(job, "you lifted four times");
        this.augmented.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "three times this week", 3, 200));

        await this.Subject().RunAsync(job, CancellationToken.None);

        await this.augmented.Received(1).RunAsync(
            Arg.Is<string>(prompt => prompt.Contains("you lifted four times", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Change_Only_Job_With_Nothing_New_Should_Surface_Nothing_And_Record_Its_Silence()
    {
        var job = Job(null, onlyWhenNew: true);
        this.Remembers(job, "Rust 2.0 released");
        this.augmented.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "NOTHING NEW", 3, 200));

        await this.Subject().RunAsync(job, CancellationToken.None);

        await this.surfacings.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
        await this.runs.Received(1).RecordAsync(Arg.Is<ScheduledJobRun>(run => !run.Delivered), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Change_Only_Discord_Job_Should_Be_Delivered_Quietly_Once_It_Has_History()
    {
        var job = Job("discord:1", onlyWhenNew: true);
        this.Remembers(job, "Rust 2.0 released");
        this.discord.DeliverAsync(job, Arg.Any<string>(), true, Arg.Any<CancellationToken>()).Returns("NOTHING NEW");

        await this.Subject().RunAsync(job, CancellationToken.None);

        await this.discord.Received(1).DeliverAsync(job, Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await this.runs.Received(1).RecordAsync(Arg.Is<ScheduledJobRun>(run => !run.Delivered), Arg.Any<CancellationToken>());
    }
}
