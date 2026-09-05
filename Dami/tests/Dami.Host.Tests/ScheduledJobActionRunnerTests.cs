using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Core.Frontier;
using Dami.Core.Scheduling;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

/// <summary>A Prompt job never touches the local model (ADR-0028) and goes where it was asked.</summary>
public sealed class ScheduledJobActionRunnerTests
{
    private readonly IScheduledPromptDelivery discord = Substitute.For<IScheduledPromptDelivery>();
    private readonly IAugmentedTurn augmented = Substitute.For<IAugmentedTurn>();
    private readonly ISurfacingQueue surfacings = Substitute.For<ISurfacingQueue>();

    private static ScheduledJob Job(string? delivery) => new(
        Guid.NewGuid(), "weekly summary", "d", ScheduledJobKind.Prompt, "summarise my week", [],
        "0 8 * * 1", "America/Chicago", ScheduledJobStatus.Active, DateTimeOffset.UnixEpoch,
        null, null, null, null, delivery);

    private ScheduledJobActionRunner Subject()
    {
        this.discord.Handles(Arg.Is<string?>(key => key != null && key.StartsWith("discord:", StringComparison.Ordinal)))
            .Returns(true);
        return new ScheduledJobActionRunner([this.discord], this.augmented, this.surfacings, TimeProvider.System);
    }

    [Fact]
    public async Task A_Job_With_A_Channel_Should_Be_Delivered_There()
    {
        var job = Job("discord:1");

        await this.Subject().RunAsync(job, CancellationToken.None);

        await this.discord.Received(1).DeliverAsync(job, Arg.Any<CancellationToken>());
        await this.augmented.DidNotReceive().RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Job_Without_A_Channel_Should_Be_Answered_By_The_Frontier_And_Surfaced()
    {
        this.augmented.RunAsync("summarise my week", Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "a quiet week", 3, 200));

        await this.Subject().RunAsync(Job(null), CancellationToken.None);

        await this.surfacings.Received(1).EnqueueAsync(
            Arg.Is<Surfacing>(surfacing =>
                surfacing.ServiceName == "scheduled-job"
                && surfacing.Title == "weekly summary"
                && surfacing.Body == "a quiet week"),
            Arg.Any<CancellationToken>());
        await this.discord.DidNotReceive().DeliverAsync(Arg.Any<ScheduledJob>(), Arg.Any<CancellationToken>());
    }
}
