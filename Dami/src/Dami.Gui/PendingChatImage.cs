using Avalonia.Media.Imaging;

namespace Dami.Gui;

/// <summary>One staged image: transport bytes plus the preview shown in the composer.</summary>
public sealed class PendingChatImage
{
    private readonly Lazy<Bitmap> thumbnail;

    /// <summary>Creates a staged preview.</summary>
    public PendingChatImage(DirectChatImage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        this.Request = request;
        this.thumbnail = new Lazy<Bitmap>(() => new Bitmap(new MemoryStream(request.Bytes)));
    }

    /// <summary>The image sent with the next direct turn.</summary>
    public DirectChatImage Request { get; }

    /// <summary>The decoded preview.</summary>
    public Bitmap Thumbnail => this.thumbnail.Value;

    /// <summary>Accessible hover text.</summary>
    public string FileName => this.Request.FileName;
}
