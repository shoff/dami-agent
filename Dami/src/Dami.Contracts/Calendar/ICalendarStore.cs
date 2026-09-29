namespace Dami.Contracts.Calendar;

/// <summary>One occurrence of an event on Steve's calendar.</summary>
/// <param name="EventKey">The event's UID and this occurrence's start: stable across fetches.</param>
/// <param name="StartsAt">When it starts; midnight local for an all-day event.</param>
/// <param name="EndsAt">When it ends, when the calendar says.</param>
/// <param name="AllDay">Whether it is a date rather than a time.</param>
/// <param name="Summary">Its title.</param>
/// <param name="Location">Where, when the calendar says.</param>
public sealed record CalendarEvent(
    string EventKey, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AllDay, string Summary, string? Location);

/// <summary>Steve's calendar as last read, for a window ahead. Local only; read-only at the source.</summary>
public interface ICalendarStore
{
    /// <summary>Replaces everything starting in [<paramref name="from"/>, <paramref name="to"/>) with <paramref name="events"/>.</summary>
    Task ReplaceAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<CalendarEvent> events, CancellationToken cancellationToken);

    /// <summary>Occurrences starting in [<paramref name="from"/>, <paramref name="to"/>), earliest first.</summary>
    Task<IReadOnlyList<CalendarEvent>> BetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
