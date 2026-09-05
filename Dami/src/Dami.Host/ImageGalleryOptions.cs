namespace Dami.Host;

/// <summary>Local storage and identity inputs for Dami's portrait gallery.</summary>
public sealed class ImageGalleryOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "ImageGallery";

    /// <summary>Durable local gallery directory.</summary>
    public string Directory { get; set; } = "/home/steve/Data/dami-gallery";

    /// <summary>Existing portraits copied into the gallery on first use.</summary>
    public List<string> SeedFiles { get; set; } = [];

    /// <summary>The single approved identity reference sent with generation requests.</summary>
    public string CanonicalReferencePath { get; set; } = string.Empty;
}
