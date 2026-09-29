using Dami.Proactive.Calendar;
using Xunit;

namespace Dami.Proactive.Tests.Calendar;

/// <summary>A Google "secret address in iCal format" feed, read into the occurrences of a window.</summary>
public sealed class CalendarReaderTests
{
    private static readonly TimeZoneInfo chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private const string FEED = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        BEGIN:VEVENT
        DTSTART:20260930T143000Z
        DTEND:20260930T150000Z
        UID:dentist@google.com
        SUMMARY:Dentist
        LOCATION:Main St Dental\, Lakeville
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;VALUE=DATE:20261001
        DTEND;VALUE=DATE:20261002
        UID:bday@google.com
        SUMMARY:Mom's birthday
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=America/Chicago:20260915T090000
        DTEND;TZID=America/Chicago:20260915T093000
        RRULE:FREQ=WEEKLY;BYDAY=TU
        UID:standup@google.com
        SUMMARY:Standup
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261201T150000Z
        DTEND:20261201T160000Z
        UID:later@google.com
        SUMMARY:Too far out
        END:VEVENT
        END:VCALENDAR
        """;

    private static readonly DateTimeOffset from = new(2026, 9, 29, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset to = from.AddDays(14);

    [Fact]
    public void Should_Read_Timed_All_Day_And_Recurring_Occurrences_In_The_Window_In_Order()
    {
        var events = CalendarReader.Read(FEED, from, to, chicago);

        Assert.Equal(
            ["Standup", "Dentist", "Mom's birthday", "Standup"],
            events.Select(item => item.Summary));
        var dentist = events.Single(item => item.Summary == "Dentist");
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 14, 30, 0, TimeSpan.Zero), dentist.StartsAt);
        Assert.Equal("Main St Dental, Lakeville", dentist.Location);
        Assert.False(dentist.AllDay);
        Assert.True(events.Single(item => item.Summary == "Mom's birthday").AllDay);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 14, 0, 0, TimeSpan.Zero), events[0].StartsAt);
    }

    [Fact]
    public void Each_Occurrence_Should_Have_Its_Own_Key()
    {
        var events = CalendarReader.Read(FEED, from, to, chicago);

        Assert.Equal(events.Count, events.Select(item => item.EventKey).Distinct().Count());
    }

    [Fact]
    public void A_Feed_That_Is_Not_A_Calendar_Should_Read_As_Nothing()
    {
        Assert.Empty(CalendarReader.Read("<html>sign in</html>", from, to, chicago));
    }
}
