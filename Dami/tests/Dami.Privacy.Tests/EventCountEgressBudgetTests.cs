using Dami.Contracts.Events;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Privacy.Tests;

/// <summary>The C5 alarm: refuse past the bound, surface exactly once at the trip.</summary>
public sealed class EventCountEgressBudgetTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly IEgressMeter egressMeter = Substitute.For<IEgressMeter>();
    private readonly ISurfacingQueue surfacingQueue = Substitute.For<ISurfacingQueue>();

    [Fact]
    public async Task FindRefusalAsync_Should_Allow_Under_Both_Bounds()
    {
        this.Count(attempts: 5);

        Assert.Null(await this.CreateBudget().FindRefusalAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Refuse_At_The_Hourly_Bound()
    {
        this.Count(attempts: 30);

        var refusal = await this.CreateBudget().FindRefusalAsync(CancellationToken.None);

        Assert.Contains("hour", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Refuse_At_The_Daily_Bound()
    {
        this.egressMeter
            .CountRequestsSinceAsync(now.AddHours(-1), Arg.Any<CancellationToken>())
            .Returns(2);
        this.egressMeter
            .CountRequestsSinceAsync(now.AddDays(-1), Arg.Any<CancellationToken>())
            .Returns(200);

        var refusal = await this.CreateBudget().FindRefusalAsync(CancellationToken.None);

        Assert.Contains("day", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Surface_Once_On_The_First_Refusal_Even_Past_The_Bound()
    {
        this.Count(attempts: 45);

        await this.CreateBudget().FindRefusalAsync(CancellationToken.None);

        await this.surfacingQueue.Received(1).EnqueueAsync(
            Arg.Is<Surfacing>(s => s.ServiceName == "egress-budget"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Stay_Quiet_While_Still_Tripped()
    {
        this.Count(attempts: 45);
        var budget = this.CreateBudget();
        await budget.FindRefusalAsync(CancellationToken.None);

        await budget.FindRefusalAsync(CancellationToken.None);

        await this.surfacingQueue.Received(1).EnqueueAsync(
            Arg.Any<Surfacing>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Surface_Again_After_The_Budget_Recovers()
    {
        var budget = this.CreateBudget();
        this.Count(attempts: 45);
        await budget.FindRefusalAsync(CancellationToken.None);
        this.Count(attempts: 3);
        await budget.FindRefusalAsync(CancellationToken.None);
        this.Count(attempts: 45);

        await budget.FindRefusalAsync(CancellationToken.None);

        await this.surfacingQueue.Received(2).EnqueueAsync(
            Arg.Any<Surfacing>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindRefusalAsync_Should_Not_Surface_While_Under_Budget()
    {
        this.Count(attempts: 3);

        await this.CreateBudget().FindRefusalAsync(CancellationToken.None);

        await this.surfacingQueue.DidNotReceive().EnqueueAsync(
            Arg.Any<Surfacing>(), Arg.Any<CancellationToken>());
    }

    private void Count(int attempts)
    {
        this.egressMeter
            .CountRequestsSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(attempts);
    }

    private EventCountEgressBudget CreateBudget()
    {
        return new EventCountEgressBudget(
            this.egressMeter, this.surfacingQueue,
            Options.Create(new EgressBudgetOptions { MaxPerHour = 30, MaxPerDay = 200 }),
            new FakeTimeProvider(now), NullLogger<EventCountEgressBudget>.Instance);
    }
}
