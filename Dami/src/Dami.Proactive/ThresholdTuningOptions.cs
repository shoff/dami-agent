namespace Dami.Proactive;

/// <summary>Bounds and gain for reaction-driven threshold tuning (H8).</summary>
public sealed class ThresholdTuningOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "ThresholdTuning";

    /// <summary>Reactions considered, newest first.</summary>
    public int Window { get; set; } = 30;

    /// <summary>Below this many reactions the base threshold is used untouched.</summary>
    public int MinimumReactions { get; set; } = 5;

    /// <summary>How strongly the reaction lean moves the threshold.</summary>
    public double Gain { get; set; } = 0.2;

    /// <summary>Most the threshold may rise above its base (bad-heavy reactions).</summary>
    public double MaxRaise { get; set; } = 0.25;

    /// <summary>Most the threshold may drop below its base (good-heavy reactions).</summary>
    public double MaxLower { get; set; } = 0.10;
}
