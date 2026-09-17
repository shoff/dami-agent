using Avalonia;

namespace Dami.Gui;

/// <summary>Geometry for a fitted or zoomed image, independent of its native renderer.</summary>
public sealed class ImageViewport
{
    private readonly Size imageSize;
    private Size viewport;
    private Vector offset;
    private bool fitted = true;

    /// <summary>Creates a viewport for the image's pixel dimensions.</summary>
    public ImageViewport(Size imageSize) => this.imageSize = imageSize;

    /// <summary>The display scale relative to the original pixels.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>The scaled image rectangle in viewport coordinates.</summary>
    public Rect ImageBounds => new(
        ((this.viewport.Width - (this.imageSize.Width * this.Scale)) / 2) + this.offset.X,
        ((this.viewport.Height - (this.imageSize.Height * this.Scale)) / 2) + this.offset.Y,
        this.imageSize.Width * this.Scale, this.imageSize.Height * this.Scale);

    /// <summary>Fits the complete image without enlarging a small source.</summary>
    public void Fit(Size viewport)
    {
        if (!IsMeasured(viewport))
        {
            return;
        }

        this.viewport = viewport;
        this.offset = default;
        this.fitted = true;
        this.Scale = Math.Min(1, Math.Min(viewport.Width / this.imageSize.Width, viewport.Height / this.imageSize.Height));
    }

    /// <summary>Changes scale while preserving the source pixel under the pointer.</summary>
    public void ZoomAt(double scale, Point anchor)
    {
        var fit = Math.Min(this.viewport.Width / this.imageSize.Width, this.viewport.Height / this.imageSize.Height);
        scale = Math.Clamp(scale, Math.Min(0.05, fit), 8);
        var source = (anchor - this.ImageBounds.Position) / this.Scale;
        this.Scale = scale;
        this.fitted = false;
        var position = anchor - (source * scale);
        this.offset = new Vector(
            position.X - ((this.viewport.Width - (this.imageSize.Width * scale)) / 2),
            position.Y - ((this.viewport.Height - (this.imageSize.Height * scale)) / 2));
        this.ClampOffset();
    }

    /// <summary>Moves a magnified image without letting an edge drift into empty space.</summary>
    public void Pan(Vector delta)
    {
        this.offset += delta;
        this.ClampOffset();
    }

    /// <summary>Reflows a fitted image while preserving explicit zoom choices.</summary>
    public void Resize(Size viewport)
    {
        if (!IsMeasured(viewport))
        {
            return;
        }

        if (this.fitted)
        {
            this.Fit(viewport);
        }
        else
        {
            this.viewport = viewport;
            this.ClampOffset();
        }
    }

    private void ClampOffset()
    {
        var x = Math.Max(0, ((this.imageSize.Width * this.Scale) - this.viewport.Width) / 2);
        var y = Math.Max(0, ((this.imageSize.Height * this.Scale) - this.viewport.Height) / 2);
        this.offset = new Vector(Math.Clamp(this.offset.X, -x, x), Math.Clamp(this.offset.Y, -y, y));
    }

    private static bool IsMeasured(Size size) =>
        size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height);
}
