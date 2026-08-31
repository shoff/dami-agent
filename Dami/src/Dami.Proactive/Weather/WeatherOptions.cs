namespace Dami.Proactive.Weather;

/// <summary>The weather sources (H14). Defaults are Lakeville, MN.</summary>
/// <remarks>
/// The gridpoint (MPX/109,57) and zone (MNZ070) were resolved from the NWS points API
/// on 2026-08-30 for city-level coordinates already public in this repository's civic
/// configuration. The queries carry nothing else.
/// </remarks>
public sealed class WeatherOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Weather";

    /// <summary>The gridpoint forecast.</summary>
    public string ForecastUrl { get; set; } =
        "https://api.weather.gov/gridpoints/MPX/109,57/forecast";

    /// <summary>Active alerts for the forecast zone.</summary>
    public string AlertsUrl { get; set; } =
        "https://api.weather.gov/alerts/active/zone/MNZ070";

    /// <summary>Local offset from UTC, hours. The DST wobble is fine for an hour histogram.</summary>
    public int LocalUtcOffsetHours { get; set; } = -5;

    /// <summary>How many days of daytime forecast to record each pass.</summary>
    public int ForecastDays { get; set; } = 3;

    /// <summary>Confidence carried by each surfacing.</summary>
    public double Confidence { get; set; } = 0.7;
}
