namespace Dami.Proactive.Gallery;

/// <summary>The gallery-curator pass (migration 040): index the folder, caption, embed.</summary>
/// <remarks>
/// On by default: everything it does is loopback inference over files already on this
/// host, so there is no bill and nothing leaves. Captioning is capped per pass because
/// the vision model shares the card with everything else.
/// </remarks>
public sealed class GalleryCuratorOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "GalleryCurator";

    /// <summary>Whether the pass runs at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The Gallery folder. The Host's <c>ImageGallery:Directory</c> must agree.</summary>
    public string Directory { get; set; } = "/home/steve/Data/dami-gallery";

    /// <summary>How many pictures the vision model describes in one pass.</summary>
    public int MaxCaptionsPerPass { get; set; } = 40;

    /// <summary>The identity anchor's file name, so it is marked rather than guessed.</summary>
    public string CanonicalFileName { get; set; } = "openai_gpt-image-2-medium_20260726_140042_9808fb1e.png";
}
