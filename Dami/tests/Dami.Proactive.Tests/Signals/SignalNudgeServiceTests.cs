using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Signals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Signals;

/// <summary>One fact, with its window and count, or nothing. A trend, never a grade.</summary>
public sealed class SignalNudgeServiceTests
{
    // 03:00 UTC on the 16th is the evening of the 15th in Chicago; "yesterday" is the 14th.
    private static readonly DateTimeOffset now = new(2026, 9, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly yesterday = new(2026, 9, 14);

    private readonly IDailySeriesSource source = Substitute.For<IDailySeriesSource>();
    private readonly List<DailySeries> series = [];
    private readonly SignalsOptions options = new() { NudgeWindowDays = 42, NudgeMinimumPoints = 5 };

    [Fact]
    public async Task RunPassAsync_Should_Surface_Yesterdays_High()
    {
        this.series.Add(Volume(1000, 1200, 900, 1100, 1300, 2600));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("highest", result.Surfacings[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Say_How_Many_Days_Backed_It()
    {
        this.series.Add(Volume(1000, 1200, 900, 1100, 1300, 2600));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("6 days with data", result.Surfacings[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_When_Yesterday_Was_Ordinary()
    {
        this.series.Add(Volume(1000, 1200, 900, 1100, 1300, 1150));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_Below_The_Point_Floor()
    {
        this.series.Add(Volume(1000, 1200, 2600));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Ignore_A_Day_Without_Data_As_A_Low()
    {
        // Rest days are zero, not "the lowest volume in six weeks".
        this.series.Add(Volume(1000, 1200, 900, 1100, 1300, 0));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Surface_At_Most_One_Fact()
    {
        this.series.Add(Volume(1000, 1200, 900, 1100, 1300, 2600));
        this.series.Add(new DailySeries("commits", "commits", "commits", Points(3, 4, 2, 5, 3, 12)));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Read_The_Window_Ending_Yesterday_In_The_Owners_Zone()
    {
        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.source.Received(1).ReadAsync(
            yesterday.AddDays(-41), yesterday, Arg.Any<TimeZoneInfo>(), Arg.Any<CancellationToken>());
    }

    private static DailySeries Volume(params double[] values) =>
        new("gym-volume", "gym volume", "lb", Points(values));

    /// <summary>The last value lands on yesterday; earlier ones on the days before it.</summary>
    private static List<DailyPoint> Points(params double[] values)
    {
        var points = new List<DailyPoint>();
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] != 0)
            {
                points.Add(new DailyPoint(yesterday.AddDays(index - values.Length + 1), values[index]));
            }
        }

        return points;
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private SignalNudgeService CreateService()
    {
        this.source.ReadAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<TimeZoneInfo>(), Arg.Any<CancellationToken>())
            .Returns(this.series);
        return new SignalNudgeService(
            [this.source], Options.Create(this.options), new FakeTimeProvider(now),
            NullLogger<SignalNudgeService>.Instance);
    }
}
