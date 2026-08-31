using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Weather;

public sealed class WeatherCollectorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 20, 0, 0, TimeSpan.Zero);

    private const string FORECAST = """
        { "properties": { "periods": [
            { "name": "Monday", "startTime": "2026-08-31T06:00:00-05:00", "isDaytime": true,
              "temperature": 72, "windSpeed": "8 mph", "shortForecast": "Sunny",
              "probabilityOfPrecipitation": { "value": 10 } },
            { "name": "Monday Night", "startTime": "2026-08-31T18:00:00-05:00", "isDaytime": false,
              "temperature": 60, "windSpeed": "5 mph", "shortForecast": "Clear",
              "probabilityOfPrecipitation": { "value": 5 } } ] } }
        """;

    private const string ALERTS = """
        { "features": [
            { "properties": { "event": "Severe Thunderstorm Watch", "severity": "Severe",
                              "headline": "Severe Thunderstorm Watch until 9 PM CDT" } } ] }
        """;

    private readonly IEgressClient egress = Substitute.For<IEgressClient>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public WeatherCollectorServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync());
        this.Answer("forecast", FORECAST);
        this.Answer("alerts", ALERTS);
    }

    [Fact]
    public async Task Should_Record_Daytime_Periods_Only()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var forecasts = this.written.FindAll(fact => fact.Category == "forecast");
        Assert.Contains("Monday:", Assert.Single(forecasts).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Surface_A_Severe_Alert()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains(
            "Severe Thunderstorm Watch",
            Assert.Single(result.Surfacings).Title,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Not_Resurface_An_Alert_Already_On_Record()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);
        this.store.TimelineAsync("weather", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(this.written.ToArray()));

        var second = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(second.Surfacings);
    }

    [Fact]
    public async Task Should_Survive_A_Refused_Source_And_Read_The_Other()
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains("forecast", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns<EgressResponse>(_ => throw new EgressRefusedException("host not allowlisted"));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            (ProactiveStatus.Completed, 1),
            (result.Status, result.Surfacings.Count));
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private void Answer(string urlPart, string body)
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains(urlPart, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, body));
    }

    private WeatherCollectorService Service()
    {
        return new WeatherCollectorService(
            this.store, this.egress, Options.Create(new WeatherOptions()),
            new FakeTimeProvider(now), NullLogger<WeatherCollectorService>.Instance);
    }
}
