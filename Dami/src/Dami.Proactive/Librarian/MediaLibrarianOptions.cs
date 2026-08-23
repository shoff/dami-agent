namespace Dami.Proactive.Librarian;

/// <summary>Where the librarian looks and where its proposals land.</summary>
public sealed class MediaLibrarianOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "MediaLibrarian";

    /// <summary>Directories to survey. Empty means the service stays quiet.</summary>
    public IList<string> RootPaths { get; } = [];

    /// <summary>Where proposal manifests are written.</summary>
    /// <remarks>
    /// Writing a manifest here is the ONLY write this service performs, and the manifest
    /// is the propose mechanism itself — the same shape as the staging registry for
    /// self-authored tools (D-016). Nothing under a root path is ever touched.
    /// </remarks>
    public string ManifestDirectory { get; set; } = "/home/steve/Data/dami-manifests";

    /// <summary>The most files one pass will survey across all roots.</summary>
    public int MaxFilesPerPass { get; set; } = 5000;

    /// <summary>Fewer loose files than this and the pass stays quiet.</summary>
    public int MinimumLooseFiles { get; set; } = 10;

    /// <summary>Whether image proposals get a local-vision caption and tags.</summary>
    /// <remarks>Off by default: ~1.4 s per image warm, and the model loads on demand.</remarks>
    public bool VisionEnabled { get; set; }

    /// <summary>The most images one pass will caption.</summary>
    public int MaxCaptionsPerPass { get; set; } = 50;
}
