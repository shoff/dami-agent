namespace Dami.Proactive.Signals;

/// <summary>Windows and floors for the single-fact nudge and the correlation card.</summary>
/// <remarks>
/// The floors are what make these honest: a "highest in six weeks" over four data points
/// is noise, and a correlation over ten days is a coincidence. The literature the catalog
/// drew on praises one surprising fact with its evidence and admitted uncertainty; every
/// surfacing here carries n and the window, and the nudge never grades a day.
/// </remarks>
public sealed class SignalsOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Signals";

    /// <summary>The owner's time zone; every series is bucketed by it.</summary>
    public string TimeZone { get; set; } = "America/Chicago";

    /// <summary>Days the nudge looks back, ending yesterday.</summary>
    public int NudgeWindowDays { get; set; } = 42;

    /// <summary>Days with data a series needs in the window before a high or low is worth saying.</summary>
    public int NudgeMinimumPoints { get; set; } = 12;

    /// <summary>Days the weekly card looks back, ending yesterday.</summary>
    public int CorrelationWindowDays { get; set; } = 84;

    /// <summary>Aligned days a pair needs before r is computed.</summary>
    public int CorrelationMinimumDays { get; set; } = 28;

    /// <summary>Days with data each series needs in the window; below it the pair is skipped.</summary>
    public int CorrelationMinimumActiveDays { get; set; } = 8;

    /// <summary>The |r| below which nothing is said.</summary>
    public double MinimumCorrelation { get; set; } = 0.5;

    /// <summary>The repository whose commits form the <c>commits</c> series.</summary>
    public string RepoPath { get; set; } = "/home/steve/dev/dami-agent";

    /// <summary>The time zone, resolved.</summary>
    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(this.TimeZone);
}
