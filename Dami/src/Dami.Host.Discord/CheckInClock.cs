using Dami.Gateway.Discord;

namespace Dami.Host.Discord;

/// <summary>The day's check-in hour, shared by everything that speaks at it.</summary>
internal static class CheckInClock
{
    /// <summary>Today's <see cref="DiscordOptions.CheckInHour"/> in the configured zone, as an instant.</summary>
    public static DateTimeOffset DueToday(DiscordOptions options, DateTimeOffset now)
    {
        var (zone, local) = Local(options, now);
        var at = local.Date.AddHours(options.CheckInHour);
        return new DateTimeOffset(at, zone.GetUtcOffset(at));
    }

    /// <summary>The day of the week in the configured zone.</summary>
    public static DayOfWeek Today(DiscordOptions options, DateTimeOffset now) => Local(options, now).Local.DayOfWeek;

    private static (TimeZoneInfo Zone, DateTimeOffset Local) Local(DiscordOptions options, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.CheckInTimeZone);
        return (zone, TimeZoneInfo.ConvertTime(now, zone));
    }
}
