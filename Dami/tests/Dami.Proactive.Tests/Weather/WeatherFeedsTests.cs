using Dami.Proactive.Weather;
using Xunit;

namespace Dami.Proactive.Tests.Weather;

public sealed class WeatherFeedsTests
{
    private const string FORECAST = """
        { "properties": { "periods": [
            { "name": "Monday", "startTime": "2026-08-31T06:00:00-05:00", "isDaytime": true,
              "temperature": 88, "windSpeed": "5 to 10 mph", "shortForecast": "Partly Sunny",
              "probabilityOfPrecipitation": { "value": 5 } },
            { "name": "Monday Night", "startTime": "2026-08-31T18:00:00-05:00", "isDaytime": false,
              "temperature": 66, "windSpeed": "5 mph", "shortForecast": "Clear",
              "probabilityOfPrecipitation": { "value": null } } ] } }
        """;

    private const string ALERTS = """
        { "features": [
            { "properties": { "event": "Severe Thunderstorm Watch", "severity": "Severe",
                              "headline": "Severe Thunderstorm Watch until 9 PM CDT" } },
            { "properties": { "event": "Air Quality Alert", "severity": "Minor",
                              "headline": "Air Quality Alert" } } ] }
        """;

    [Fact]
    public void ParseForecast_Should_Read_A_Period_With_The_Top_Wind()
    {
        var periods = WeatherFeeds.ParseForecast(FORECAST);

        Assert.Equal(
            ("Monday", true, 88, 10, 5, "Partly Sunny"),
            (periods[0].Name, periods[0].IsDaytime, periods[0].TemperatureF,
                periods[0].WindMph, periods[0].PrecipPct, periods[0].Short));
    }

    [Fact]
    public void ParseForecast_Should_Treat_A_Null_Precipitation_As_Zero()
    {
        Assert.Equal(0, WeatherFeeds.ParseForecast(FORECAST)[1].PrecipPct);
    }

    [Fact]
    public void ParseAlerts_Should_Read_Event_And_Severity()
    {
        var alerts = WeatherFeeds.ParseAlerts(ALERTS);

        Assert.Equal(
            (2, "Severe Thunderstorm Watch", "Severe"),
            (alerts.Count, alerts[0].Event, alerts[0].Severity));
    }

    [Fact]
    public void ReadForecastFact_Should_Round_Trip_The_Collector_Description()
    {
        var description = WeatherFeeds.ForecastDescription(
            new ForecastPeriod(
                "Monday", new DateTimeOffset(2026, 8, 31, 6, 0, 0, TimeSpan.FromHours(-5)),
                true, 88, 10, 5, "Partly Sunny"));

        var read = WeatherFeeds.ReadForecastFact(description);

        Assert.Equal((new DateOnly(2026, 8, 31), 88, 10, 5), read);
    }

    [Fact]
    public void ReadForecastFact_Should_Refuse_A_Foreign_Description()
    {
        Assert.Null(WeatherFeeds.ReadForecastFact("Severe Thunderstorm Watch until 9 PM"));
    }
}
