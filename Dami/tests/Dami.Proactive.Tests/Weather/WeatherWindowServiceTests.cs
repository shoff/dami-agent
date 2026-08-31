using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Weather;

public sealed class WeatherWindowServiceTests
{
    // 20:00 UTC on the 30th; "tomorrow" local (-5) is the 31st.
    private static readonly DateTimeOffset now = new(2026, 8, 30, 20, 0, 0, TimeSpan.Zero);

    private readonly IFitnessStore fitness = Substitute.For<IFitnessStore>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public WeatherWindowServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.fitness.SnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new FitnessSnapshot(
                [Cardio(new DateTimeOffset(2026, 8, 20, 22, 0, 0, TimeSpan.Zero)),
                 Cardio(new DateTimeOffset(2026, 8, 24, 22, 0, 0, TimeSpan.Zero))],
                [], []));
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(Forecast("forecast 2026-08-31 Monday: 72F, wind 8 mph, precip 10%, Sunny")));
    }

    [Fact]
    public async Task Should_Surface_A_Good_Window_For_Tomorrow_With_The_Usual_Hour()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Equal(
            (true, true),
            (surfacing.Title.Contains("outdoor cardio", StringComparison.Ordinal),
                surfacing.Body.Contains("17:00", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Should_Stay_Quiet_When_Tomorrow_Is_Bad()
    {
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(Forecast("forecast 2026-08-31 Monday: 95F, wind 8 mph, precip 10%, Sunny")));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task Should_Not_Resurface_The_Same_Day()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                Forecast("forecast 2026-08-31 Monday: 72F, wind 8 mph, precip 10%, Sunny"),
                this.written[0]));

        var second = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(second.Surfacings);
    }

    [Fact]
    public async Task Should_Stay_Quiet_With_No_Forecast_For_Tomorrow()
    {
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync());

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((0, 0), (result.Surfacings.Count, this.written.Count));
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static FitnessCardioSession Cardio(DateTimeOffset at) => new(
        Guid.NewGuid(), at, "treadmill", 1800, null, null, null, null, false, null);

    private static DomainFact Forecast(string description) => new(
        Guid.NewGuid(), "weather", new DateOnly(2026, 8, 31), "forecast", description,
        "weather-collector", now);

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private WeatherWindowService Service()
    {
        return new WeatherWindowService(
            this.fitness, this.store, Options.Create(new WeatherOptions()),
            new FakeTimeProvider(now), NullLogger<WeatherWindowService>.Instance);
    }
}
