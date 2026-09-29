namespace Dami.Proactive.Calendar;

/// <summary>Where Steve's calendar is read from.</summary>
public sealed class CalendarOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "Calendar";

    /// <summary>
    /// Google's "secret address in iCal format". A credential: it lives in
    /// <c>~/.config/dami/proactive.env</c> as <c>Calendar__IcsUrl</c>, never in the repository.
    /// Empty turns the collector off. Its host must be on the egress allowlist.
    /// </summary>
    public string IcsUrl { get; set; } = string.Empty;

    /// <summary>The zone "today" and all-day events are read in.</summary>
    public string TimeZone { get; set; } = "America/Chicago";

    /// <summary>Days ahead of today mirrored.</summary>
    public int DaysAhead { get; set; } = 14;
}
