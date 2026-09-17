namespace Dami.Proactive.Health;

/// <summary>What counts as a lapsed INR interval.</summary>
public sealed class InrCadenceOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "InrCadence";

    /// <summary>Recorded checks with a value needed before an interval is known at all.</summary>
    public int MinimumChecks { get; set; } = 3;

    /// <summary>The kept interval times this is the lapse; 1.5 turns "every three weeks" into "four and a half".</summary>
    public double LapseFactor { get; set; } = 1.5;

    /// <summary>Health timeline rows read per pass.</summary>
    public int TimelineRows { get; set; } = 400;

    /// <summary>Recent surfacings scanned to avoid saying it twice for the same last check.</summary>
    public int RecentSurfacings { get; set; } = 200;

    /// <summary>The owner's time zone, for "today".</summary>
    public string TimeZone { get; set; } = "America/Chicago";
}
