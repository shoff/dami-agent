using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Signals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Signals;

/// <summary>One correlation a week, with r and n, or nothing.</summary>
public sealed class CorrelationCardServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly yesterday = new(2026, 9, 14);

    private readonly List<DailySeries> series = [];
    private readonly SignalsOptions options = new()
    {
        CorrelationWindowDays = 30, CorrelationMinimumDays = 20, CorrelationMinimumActiveDays = 5, MinimumCorrelation = 0.5,
    };

    [Fact]
    public async Task RunPassAsync_Should_Surface_A_Strong_Pair_With_Its_Numbers()
    {
        this.series.Add(Series("gym-volume", "gym volume", day => 100.0 + (day * 10)));
        this.series.Add(Series("late-night-turns", "late-night conversations", day => 20.0 - day));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("r = -1.00 over 30 days", result.Surfacings[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Name_The_Direction()
    {
        this.series.Add(Series("gym-volume", "gym volume", day => 100.0 + (day * 10)));
        this.series.Add(Series("late-night-turns", "late-night conversations", day => 20.0 - day));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("More gym volume goes with lower late-night conversations", result.Surfacings[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_When_Nothing_Correlates()
    {
        this.series.Add(Series("a", "a", day => day % 2 == 0 ? 1.0 : 5.0));
        this.series.Add(Series("b", "b", day => day % 3 == 0 ? 7.0 : 2.0));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_When_A_Series_Is_Too_Sparse()
    {
        this.series.Add(Series("gym-volume", "gym volume", day => 100.0 + (day * 10)));
        this.series.Add(Series("commits", "commits", day => day < 3 ? 20.0 - day : 0.0));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Admit_It_Is_Only_A_Correlation()
    {
        this.series.Add(Series("gym-volume", "gym volume", day => 100.0 + (day * 10)));
        this.series.Add(Series("late-night-turns", "late-night conversations", day => 20.0 - day));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("not cause", result.Surfacings[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunPassAsync_Should_Not_Pair_Two_Series_From_The_Same_Source()
    {
        // Gym volume against working sets is arithmetic, not a finding (r = 0.96 live on 2026-09-16).
        var gym = Substitute.For<IDailySeriesSource>();
        gym.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<TimeZoneInfo>(), Arg.Any<CancellationToken>())
            .Returns(new List<DailySeries>
            {
                Series("gym-volume", "gym volume", day => 100.0 + (day * 10)),
                Series("gym-sets", "working sets", day => 10.0 + day),
            });
        var service = new CorrelationCardService(
            [gym], Options.Create(this.options), new FakeTimeProvider(now), NullLogger<CorrelationCardService>.Instance);

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    private static DailySeries Series(string metric, string label, Func<int, double> value)
    {
        var points = new List<DailyPoint>();
        for (var day = 0; day < 30; day++)
        {
            var v = value(day);
            if (v != 0)
            {
                points.Add(new DailyPoint(yesterday.AddDays(day - 29), v));
            }
        }

        return new DailySeries(metric, label, "units", points);
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    /// <summary>One source per series: the card only pairs across sources.</summary>
    private CorrelationCardService CreateService()
    {
        var sources = new List<IDailySeriesSource>();
        foreach (var item in this.series)
        {
            var source = Substitute.For<IDailySeriesSource>();
            source.ReadAsync(
                    Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<TimeZoneInfo>(), Arg.Any<CancellationToken>())
                .Returns(new List<DailySeries> { item });
            sources.Add(source);
        }

        return new CorrelationCardService(
            sources, Options.Create(this.options), new FakeTimeProvider(now),
            NullLogger<CorrelationCardService>.Instance);
    }
}
