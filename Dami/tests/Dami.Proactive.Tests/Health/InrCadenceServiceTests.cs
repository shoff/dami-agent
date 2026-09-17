using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Health;

/// <summary>A heads-up from the log when the kept interval lapses; once per lapse; never a nag.</summary>
public sealed class InrCadenceServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly IHealthEventStore healthStore = Substitute.For<IHealthEventStore>();
    private readonly ISurfacingQueue surfacings = Substitute.For<ISurfacingQueue>();
    private readonly List<HealthEvent> timeline = [];
    private readonly List<Surfacing> recent = [];

    [Fact]
    public async Task RunPassAsync_Should_Surface_When_The_Kept_Interval_Has_Lapsed()
    {
        // Checks every ~21 days; the last one 40 days ago.
        this.Inr(-82, "INR 2.4");
        this.Inr(-61, "INR 2.6");
        this.Inr(-40, "INR 2.2");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Carry_The_Evidence()
    {
        this.Inr(-82, "INR 2.4");
        this.Inr(-61, "INR 2.6");
        this.Inr(-40, "INR 2.2");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("last recorded INR was 2.2 on 2026-08-07", result.Surfacings[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_Inside_The_Kept_Interval()
    {
        this.Inr(-52, "INR 2.4");
        this.Inr(-31, "INR 2.6");
        this.Inr(-10, "INR 2.2");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_With_Too_Few_Checks_To_Know_The_Interval()
    {
        this.Inr(-61, "INR 2.6");
        this.Inr(-40, "INR 2.2");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Ignore_Mentions_Without_A_Value()
    {
        this.Inr(-82, "INR checked twice weekly due to drug interaction with warfarin");
        this.Inr(-61, "INR level monitored");
        this.Inr(-40, "INR instability due to alcohol consumption");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Not_Say_It_Twice_For_The_Same_Last_Check()
    {
        this.Inr(-82, "INR 2.4");
        this.Inr(-61, "INR 2.6");
        this.Inr(-40, "INR 2.2");
        this.recent.Add(new Surfacing(
            Guid.NewGuid(), "inr-cadence", "INR check", "said already", 0.6, now.AddDays(-5)));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    private void Inr(int daysAgo, string description)
    {
        this.timeline.Add(new HealthEvent(
            Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(now.AddDays(daysAgo).UtcDateTime),
            HealthCategory.Vital, description));
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private InrCadenceService CreateService()
    {
        this.healthStore.TimelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.timeline));
        this.surfacings.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.recent));
        return new InrCadenceService(
            this.healthStore, this.surfacings, Options.Create(new InrCadenceOptions()),
            new FakeTimeProvider(now), NullLogger<InrCadenceService>.Instance);
    }

    private static async IAsyncEnumerable<T> AsAsync<T>(List<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
