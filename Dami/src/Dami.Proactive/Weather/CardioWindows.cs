using System.Globalization;

namespace Dami.Proactive.Weather;

/// <summary>Scores forecast periods for outdoor cardio. Pure, thresholds stated.</summary>
public static class CardioWindows
{
    private const int MIN_TEMPERATURE_F = 38;
    private const int MAX_TEMPERATURE_F = 85;
    private const int MAX_WIND_MPH = 20;
    private const int MAX_PRECIP_PCT = 30;

    /// <summary>The local hour cardio usually happens, from the log, or null without one.</summary>
    public static int? UsualHour(IReadOnlyList<DateTimeOffset> sessions, int utcOffsetHours)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
        {
            return null;
        }

        var counts = new int[24];
        foreach (var session in sessions)
        {
            counts[((session.UtcDateTime.Hour + utcOffsetHours) % 24 + 24) % 24]++;
        }

        var best = 0;
        for (var hour = 1; hour < 24; hour++)
        {
            if (counts[hour] > counts[best])
            {
                best = hour;
            }
        }

        return best;
    }

    /// <summary>Whether the period suits outdoor cardio, and the numbers that say so.</summary>
    public static (bool Good, string Why) Judge(ForecastPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Judge(period.TemperatureF, period.WindMph, period.PrecipPct);
    }

    /// <summary>The same judgement from bare numbers, for recorded forecast facts.</summary>
    public static (bool Good, string Why) Judge(int temperatureF, int windMph, int precipPct)
    {
        var good = temperatureF >= MIN_TEMPERATURE_F && temperatureF <= MAX_TEMPERATURE_F
            && windMph <= MAX_WIND_MPH && precipPct <= MAX_PRECIP_PCT;
        return (good, string.Create(
            CultureInfo.InvariantCulture,
            $"{temperatureF}F, wind {windMph} mph, {precipPct}% rain"));
    }
}
