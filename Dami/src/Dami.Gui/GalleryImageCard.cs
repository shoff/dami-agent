using System.Text.Json;
using Avalonia.Media.Imaging;

namespace Dami.Gui;

/// <summary>One persisted Dami portrait as presented by the Gallery tab.</summary>
public sealed class GalleryImageCard
{
    private GalleryImageCard(
        string fileName,
        DateTimeOffset createdAt,
        string prompt,
        string model,
        bool canonical,
        string caption,
        IReadOnlyList<string> tags,
        string source)
    {
        this.FileName = fileName;
        this.CreatedAt = createdAt;
        this.Prompt = prompt;
        this.Model = model;
        this.IsCanonical = canonical;
        this.Caption = caption;
        this.Tags = tags;
        this.Source = source;
    }

    /// <summary>Maps runtime metadata; the curator's fields are optional until it has looked.</summary>
    public static GalleryImageCard From(JsonElement item) => new(
        item.GetProperty("fileName").GetString() ?? string.Empty,
        item.GetProperty("createdAt").GetDateTimeOffset(),
        item.GetProperty("prompt").GetString() ?? string.Empty,
        item.GetProperty("model").GetString() ?? string.Empty,
        item.GetProperty("isCanonical").GetBoolean(),
        Optional(item, "caption") ?? string.Empty,
        TagsOf(item),
        Optional(item, "source") ?? "unknown");

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

    /// <summary>What the local vision model saw; empty until the curator has looked.</summary>
    public string Caption { get; }

    /// <summary>The curator's tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Where it came from: chat, discord, scheduled, proactive, imported, unknown.</summary>
    public string Source { get; }

    /// <summary>Decoded local image.</summary>
    public Bitmap? Image { get; set; }

    /// <summary>Compact provenance label.</summary>
    public string Badge => this.IsCanonical ? "identity anchor" : this.Model;

    /// <summary>Human-readable date.</summary>
    public string Date => this.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt");

    /// <summary>The tags as one line for the detail pane.</summary>
    public string TagLine => string.Join(" · ", this.Tags);

    /// <summary>The caption, or the prompt when the curator has not caught up.</summary>
    public string Description => this.Caption.Length > 0 ? this.Caption : this.Prompt;

    private static string? Optional(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> TagsOf(JsonElement item) =>
        item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray().Select(tag => tag.GetString() ?? string.Empty).Where(tag => tag.Length > 0).ToList()
            : [];
}
