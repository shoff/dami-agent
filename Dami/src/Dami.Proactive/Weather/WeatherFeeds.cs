using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dami.Proactive.Weather;

/// <summary>One NWS forecast period, numbers extracted.</summary>
public sealed record ForecastPeriod(
    string Name,
    DateTimeOffset Start,
    bool IsDaytime,
    int TemperatureF,
    int WindMph,
    int PrecipPct,
    string Short);

/// <summary>One active NWS alert.</summary>
public sealed record WeatherAlert(string Event, string Severity, string Headline);

/// <summary>Reads the NWS wire formats and this domain's own fact wording. Pure.</summary>
public static partial class WeatherFeeds
{
    /// <summary>The forecast's periods, in order.</summary>
    public static IReadOnlyList<ForecastPeriod> ParseForecast(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var periods = new List<ForecastPeriod>();
            foreach (var period in document.RootElement
                .GetProperty("properties").GetProperty("periods").EnumerateArray())
            {
                periods.Add(new ForecastPeriod(
                    Text(period, "name"),
                    DateTimeOffset.Parse(Text(period, "startTime"), CultureInfo.InvariantCulture),
                    period.GetProperty("isDaytime").GetBoolean(),
                    period.GetProperty("temperature").GetInt32(),
                    TopWind(Text(period, "windSpeed")),
                    Precip(period),
                    Text(period, "shortForecast")));
            }

            return periods;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException)
        {
            return [];
        }
    }

    /// <summary>The active alerts.</summary>
    public static IReadOnlyList<WeatherAlert> ParseAlerts(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var alerts = new List<WeatherAlert>();
            foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                var properties = feature.GetProperty("properties");
                alerts.Add(new WeatherAlert(
                    Text(properties, "event"),
                    Text(properties, "severity"),
                    Text(properties, "headline")));
            }

            return alerts;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return [];
        }
    }

    /// <summary>The wording the collector records — the scorer's parse counterpart.</summary>
    public static string ForecastDescription(ForecastPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"forecast {period.Start:yyyy-MM-dd} {period.Name}: {period.TemperatureF}F, "
            + $"wind {period.WindMph} mph, precip {period.PrecipPct}%, {period.Short}");
    }

    /// <summary>The numbers back out of a recorded forecast fact, or null for anything else.</summary>
    public static (DateOnly Date, int TemperatureF, int WindMph, int PrecipPct)? ReadForecastFact(
        string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        var match = ForecastFactPattern().Match(description);
        if (!match.Success)
        {
            return null;
        }

        return (
            DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));
    }

    private static int TopWind(string windSpeed)
    {
        var top = 0;
        foreach (Match match in WindNumberPattern().Matches(windSpeed))
        {
            top = Math.Max(top, int.Parse(match.Value, CultureInfo.InvariantCulture));
        }

        return top;
    }

    private static int Precip(JsonElement period) =>
        period.TryGetProperty("probabilityOfPrecipitation", out var precipitation)
            && precipitation.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    [GeneratedRegex(@"^forecast (\d{4}-\d{2}-\d{2}) .+?: (-?\d+)F, wind (\d+) mph, precip (\d+)%")]
    private static partial Regex ForecastFactPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex WindNumberPattern();
}
