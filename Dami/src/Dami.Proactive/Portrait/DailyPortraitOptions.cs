namespace Dami.Proactive.Portrait;

/// <summary>The daily portrait pass (ADR-0027), ported from the Hermes cron jobs.</summary>
/// <remarks>
/// Off by default. It generates through the Codex subscription's image tool (ADR-0029),
/// so it costs no metered key — but it is still a capability with a bill somewhere
/// behind it, and one that should be switched on deliberately rather than inherited by
/// anyone who deploys.
///
/// The prompt is configuration because it is Steve's to write, not this repository's to
/// hold: the default is a plain portrait, and anything more specific belongs in his
/// drop-in.
/// </remarks>
public sealed class DailyPortraitOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "DailyPortrait";

    /// <summary>Whether the pass runs at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where images are written. They stay on this host. Pointing this at the Gallery
    /// directory puts each pass in the Gallery tab, sidecar and all.
    /// </summary>
    public string OutputDirectory { get; set; } = "/home/steve/.local/share/dami/portraits";

    /// <summary>
    /// What to draw. <c>{slot}</c> is replaced with morning, midday or evening.
    /// </summary>
    public string PromptTemplate { get; set; } =
        "A warm, tasteful portrait of Dami, a personal AI companion, in {slot} light. "
        + "Natural composition, photographic, relaxed and friendly.";

    /// <summary>
    /// The one approved identity image, sent with every request so the portrait is of
    /// Dami and not of whoever the model imagines. Empty sends the template alone.
    /// </summary>
    public string ReferencePath { get; set; } = string.Empty;

    /// <summary>Local offset from UTC, hours, for naming the slot and the file.</summary>
    public int LocalUtcOffsetHours { get; set; } = -5;

    /// <summary>Pixel size passed to the provider.</summary>
    public string Size { get; set; } = "1024x1536";

    /// <summary>Quality passed to the provider.</summary>
    public string Quality { get; set; } = "high";

    /// <summary>Confidence carried by the surfacing.</summary>
    public double Confidence { get; set; } = 0.5;
}
