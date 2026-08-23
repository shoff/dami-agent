namespace Dami.Persistence.Proactive;

/// <summary>Configuration for the proactive tier's scarcity rules.</summary>
public sealed class ProactiveOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SECTION_NAME = "Proactive";

    /// <summary>
    /// The most surfacings one service may have pending or delivered per rolling day.
    /// </summary>
    /// <remarks>
    /// The hard cap from the risk register: "a muse that talks constantly is an
    /// infestation". Beyond it, surfacings are stored as Suppressed rather than shown.
    /// The default of 3 is a guess to be tuned on recorded reactions — it is not derived
    /// from anything, and D-021 expects the thresholds to move.
    /// </remarks>
    public int MaxSurfacingsPerServicePerDay { get; set; } = 3;
}
