using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Dami.Gui;

/// <summary>Converts native desktop image gestures into the loopback chat contract.</summary>
public static class ChatImageInput
{
    /// <summary>Provides the natural default question for an image-only turn.</summary>
    public static string? MessageOrDefault(string? text, bool hasImage)
    {
        var message = text?.Trim();
        return !string.IsNullOrEmpty(message)
            ? message
            : hasImage ? "What is in this image?" : null;
    }

    /// <summary>Whether a dropped or pasted file is a supported image.</summary>
    public static bool IsSupportedFile(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";

    /// <summary>Reads one storage file into a direct-chat attachment.</summary>
    public static async Task<DirectChatImage?> FromFileAsync(
        IStorageItem item,
        CancellationToken cancellationToken)
    {
        if (item is not IStorageFile file || !IsSupportedFile(file.Name))
        {
            return null;
        }

        await using var input = await file.OpenReadAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return new DirectChatImage(file.Name, ContentType(file.Name), output.ToArray());
    }

    /// <summary>Encodes clipboard pixels as PNG for local vision.</summary>
    public static DirectChatImage FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var output = new MemoryStream();
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
        return new DirectChatImage("clipboard.png", "image/png", output.ToArray());
    }

    private static string ContentType(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png",
        };
}
