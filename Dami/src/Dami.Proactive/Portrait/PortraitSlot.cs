using System.Globalization;

namespace Dami.Proactive.Portrait;

/// <summary>Which part of the day a portrait pass belongs to. Pure.</summary>
/// <remarks>
/// The Hermes jobs were three separate cron entries named for their times. The scheduler
/// here is interval-based and has no notion of clock time, so one service runs every
/// eight hours and names the slot from the clock when it actually runs. That keeps the
/// scheduler simple and makes the label honest: it says when the pass happened, not when
/// it was supposed to.
/// </remarks>
public static class PortraitSlot
{
    /// <summary>The slot a moment falls in, in local time.</summary>
    public static string Of(DateTimeOffset now, int utcOffsetHours)
    {
        var hour = Local(now, utcOffsetHours).Hour;
        return hour switch
        {
            >= 5 and < 12 => "morning",
            >= 12 and < 17 => "midday",
            _ => "evening",
        };
    }

    /// <summary>The file a pass writes, dated and slotted in local time.</summary>
    public static string FileNameFor(DateTimeOffset now, int utcOffsetHours)
    {
        var local = Local(now, utcOffsetHours);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"dami-{local:yyyy-MM-dd}-{Of(now, utcOffsetHours)}.png");
    }

    private static DateTimeOffset Local(DateTimeOffset now, int utcOffsetHours) =>
        now.ToUniversalTime().AddHours(utcOffsetHours);
}
