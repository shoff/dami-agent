using Dami.Proactive.Weather;
using Xunit;

namespace Dami.Proactive.Tests.Weather;

/// <summary>Forecast periods that ask Steve to do something: cover the plants, tie down the furniture (C8).</summary>
public sealed class WeatherActionsTests
{
    private static readonly DateTimeOffset now = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    private static ForecastPeriod Period(string name, int hoursAhead, bool day, int temperature, int wind) =>
        new(name, now.AddHours(hoursAhead), day, temperature, wind, 0, "Clear");

    [Fact]
    public void A_Freezing_Night_Within_A_Day_And_A_Half_Should_Be_A_Frost_Action()
    {
        var actions = WeatherFeeds.Actions(
            [Period("Tonight", 3, false, 31, 5), Period("Monday", 15, true, 55, 5), Period("Monday Night", 27, false, 38, 5)],
            now, frostF: 32, windMph: 30);

        var frost = Assert.Single(actions);
        Assert.Equal("frost", frost.Kind);
        Assert.Contains("Tonight", frost.Text, StringComparison.Ordinal);
        Assert.Contains("31°F", frost.Text, StringComparison.Ordinal);
        Assert.Equal(DateOnly.FromDateTime(now.AddHours(3).UtcDateTime), frost.Day);
    }

    [Fact]
    public void Strong_Wind_Should_Be_A_Wind_Action_And_Calm_Mild_Weather_Nothing()
    {
        var actions = WeatherFeeds.Actions(
            [Period("Tonight", 3, false, 45, 10), Period("Monday", 15, true, 60, 35)], now, frostF: 32, windMph: 30);

        Assert.Equal(["wind"], actions.Select(action => action.Kind));
        Assert.Contains("35 mph", actions[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Weather_Beyond_A_Day_And_A_Half_Should_Wait_For_A_Later_Pass()
    {
        Assert.Empty(WeatherFeeds.Actions([Period("Wednesday Night", 60, false, 20, 40)], now, frostF: 32, windMph: 30));
    }
}
