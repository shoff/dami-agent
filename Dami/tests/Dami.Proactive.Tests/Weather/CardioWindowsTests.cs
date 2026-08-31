using Dami.Proactive.Weather;
using Xunit;

namespace Dami.Proactive.Tests.Weather;

public sealed class CardioWindowsTests
{
    private static DateTimeOffset AtUtc(int day, int hourUtc) =>
        new(2026, 8, day, hourUtc, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UsualHour_Should_Find_The_Modal_Local_Hour()
    {
        // Sessions at 22:00 UTC are 17:00 local at -5 — the after-work window.
        var hour = CardioWindows.UsualHour(
            [AtUtc(20, 22), AtUtc(22, 22), AtUtc(24, 22), AtUtc(26, 13)],
            utcOffsetHours: -5);

        Assert.Equal(17, hour);
    }

    [Fact]
    public void UsualHour_Should_Say_Nothing_Without_History()
    {
        Assert.Null(CardioWindows.UsualHour([], utcOffsetHours: -5));
    }

    [Theory]
    [InlineData(72, 8, 10, true)]
    [InlineData(95, 8, 10, false)]
    [InlineData(72, 25, 10, false)]
    [InlineData(72, 8, 60, false)]
    [InlineData(30, 8, 10, false)]
    public void Judge_Should_Score_A_Period(int temperature, int wind, int precip, bool expected)
    {
        var period = new ForecastPeriod(
            "Monday", new DateTimeOffset(2026, 8, 31, 6, 0, 0, TimeSpan.FromHours(-5)),
            true, temperature, wind, precip, "Partly Sunny");

        Assert.Equal(expected, CardioWindows.Judge(period).Good);
    }
}
