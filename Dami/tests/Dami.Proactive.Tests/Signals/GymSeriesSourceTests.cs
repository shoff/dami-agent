using Dami.Contracts.Domains;
using Dami.Proactive.Signals;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Signals;

public sealed class GymSeriesSourceTests
{
    private static readonly TimeZoneInfo chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly IFitnessStore store = Substitute.For<IFitnessStore>();

    [Fact]
    public async Task ReadAsync_Should_Sum_Working_Volume_Per_Local_Day()
    {
        // 02:00 UTC on the 15th is the evening of the 14th in Chicago.
        this.Snapshot(
            Set(new DateTimeOffset(2026, 9, 15, 2, 0, 0, TimeSpan.Zero), 100, 10, warmup: false),
            Set(new DateTimeOffset(2026, 9, 15, 2, 5, 0, TimeSpan.Zero), 100, 8, warmup: false),
            Set(new DateTimeOffset(2026, 9, 15, 1, 50, 0, TimeSpan.Zero), 50, 10, warmup: true));

        var series = await new GymSeriesSource(this.store).ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), chicago, CancellationToken.None);

        Assert.Equal(new DailyPoint(new DateOnly(2026, 9, 14), 1800.0), series[0].Points[0]);
    }

    [Fact]
    public async Task ReadAsync_Should_Leave_Out_Days_Outside_The_Window()
    {
        this.Snapshot(Set(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), 100, 10, warmup: false));

        var series = await new GymSeriesSource(this.store).ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), chicago, CancellationToken.None);

        Assert.Empty(series[0].Points);
    }

    [Fact]
    public async Task ReadAsync_Should_Also_Count_Working_Sets()
    {
        this.Snapshot(
            Set(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), 100, 10, warmup: false),
            Set(new DateTimeOffset(2026, 9, 10, 12, 5, 0, TimeSpan.Zero), 100, 8, warmup: false));

        var series = await new GymSeriesSource(this.store).ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), chicago, CancellationToken.None);

        Assert.Equal(2.0, series[1].Points[0].Value);
    }

    private void Snapshot(params FitnessSet[] sets)
    {
        this.store.SnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new FitnessSnapshot([], sets, []));
    }

    private static FitnessSet Set(DateTimeOffset at, decimal weight, short reps, bool warmup) =>
        new(Guid.NewGuid(), Guid.NewGuid(), at, "bench press", "chest", 1, reps, weight, null, warmup);
}
