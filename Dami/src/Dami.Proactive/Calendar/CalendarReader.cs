using System.Globalization;
using Dami.Contracts.Calendar;
using Ical.Net.DataTypes;

namespace Dami.Proactive.Calendar;

/// <summary>Reads an iCalendar feed into the occurrences that start inside a window.</summary>
/// <remarks>
/// Recurrence is Ical.Net's: a weekly meeting defined once in 2024 has to become its
/// Tuesdays in this window, and hand-rolled RRULE expansion is where calendar code goes
/// wrong. A body that is not a calendar — a sign-in page after the secret address was
/// reset — reads as nothing rather than throwing.
/// </remarks>
public static class CalendarReader
{
    /// <summary>Occurrences starting in [<paramref name="from"/>, <paramref name="to"/>), earliest first.</summary>
    public static IReadOnlyList<CalendarEvent> Read(string feed, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(zone);
        if (!feed.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal))
        {
            return [];
        }

        var calendar = Ical.Net.Calendar.Load(feed);
        if (calendar is null)
        {
            return [];
        }

        var events = new List<CalendarEvent>();
        foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(from.UtcDateTime)))
        {
            var start = At(occurrence.Period.StartTime, zone);
            if (start >= to)
            {
                break;
            }

            if (start >= from && occurrence.Source is Ical.Net.CalendarComponents.CalendarEvent source)
            {
                events.Add(Event(occurrence, source, start, zone));
            }
        }

        return events;
    }

    private static CalendarEvent Event(
        Ical.Net.DataTypes.Occurrence occurrence, Ical.Net.CalendarComponents.CalendarEvent source, DateTimeOffset start,
        TimeZoneInfo zone) =>
        new(
            $"{source.Uid}@{start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}",
            start,
            occurrence.Period.EffectiveEndTime is { } end ? At(end, zone) : null,
            !occurrence.Period.StartTime.HasTime,
            source.Summary?.Trim() ?? "(no title)",
            string.IsNullOrWhiteSpace(source.Location) ? null : source.Location.Trim());

    /// <summary>A timed value is an instant; a date is midnight in Steve's zone.</summary>
    private static DateTimeOffset At(CalDateTime value, TimeZoneInfo zone)
    {
        if (!value.HasTime)
        {
            var midnight = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
    }
}
