using System.Text.Json;
using Avalonia.Media.Imaging;

namespace Dami.Gui;

/// <summary>One persisted Dami portrait as presented by the Gallery tab.</summary>
public sealed class GalleryImageCard
{
    private GalleryImageCard(
        string fileName, DateTimeOffset createdAt, string prompt, string model, bool canonical)
    {
        this.FileName = fileName;
        this.CreatedAt = createdAt;
        this.Prompt = prompt;
        this.Model = model;
        this.IsCanonical = canonical;
    }

    /// <summary>Maps runtime metadata.</summary>
    public static GalleryImageCard From(JsonElement item) => new(
        item.GetProperty("fileName").GetString() ?? string.Empty,
        item.GetProperty("createdAt").GetDateTimeOffset(),
        item.GetProperty("prompt").GetString() ?? string.Empty,
        item.GetProperty("model").GetString() ?? string.Empty,
        item.GetProperty("isCanonical").GetBoolean());

    /// <summary>Artifact basename.</summary>
    public string FileName { get; }

    /// <summary>Generation or import timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>User scene request, when generated in this app.</summary>
    public string Prompt { get; }

    /// <summary>Provider model provenance.</summary>
    public string Model { get; }

    /// <summary>Whether this is the configured identity anchor.</summary>
    public bool IsCanonical { get; }

    /// <summary>Decoded local image.</summary>
    public Bitmap? Image { get; set; }

    /// <summary>Compact provenance label.</summary>
    public string Badge => this.IsCanonical ? "identity anchor" : this.Model;

    /// <summary>Human-readable date.</summary>
    public string Date => this.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt");
}
