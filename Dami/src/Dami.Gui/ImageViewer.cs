using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>A full-resolution image canvas with pointer-anchored zoom and bounded pan.</summary>
public sealed class ImageViewer : Control
{
    private IImage? image;
    private ImageViewport? viewport;
    private Point? dragPosition;

    /// <summary>Creates a clipped, keyboard-accessible canvas.</summary>
    public ImageViewer()
    {
        this.Focusable = true;
        this.ClipToBounds = true;
    }

    /// <summary>Raised when the displayed scale or position changes.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The current scale relative to source pixels.</summary>
    public double Scale => this.viewport?.Scale ?? 1;

    /// <summary>The currently drawn image rectangle in canvas coordinates.</summary>
    public Rect ImageBounds => this.viewport?.ImageBounds ?? default;

    /// <summary>Displays a shared image without taking ownership of its lifetime.</summary>
    public void ShowImage(IImage image, Size pixels)
    {
        ArgumentNullException.ThrowIfNull(image);
        this.image = image;
        this.viewport = new ImageViewport(pixels);
        this.viewport.Fit(this.Bounds.Size);
        this.Changed();
    }

    /// <summary>Releases references when the viewer closes; the source remains usable.</summary>
    public void Clear()
    {
        this.image = null;
        this.viewport = null;
        this.dragPosition = null;
        this.Changed();
    }

    /// <summary>Applies fit, actual-size, zoom and pan shortcuts; leaves unrelated keys alone.</summary>
    public bool HandleKey(Key key)
    {
        var handled = this.HandleZoomKey(key) || this.HandlePanKey(key);
        if (handled)
        {
            this.Changed();
        }

        return handled;
    }

    private bool HandleZoomKey(Key key)
    {
        switch (key)
        {
            case Key.D0 or Key.NumPad0:
                this.viewport?.Fit(this.Bounds.Size);
                break;
            case Key.D1 or Key.NumPad1:
                this.viewport?.ZoomAt(1, this.Center);
                break;
            case Key.Add or Key.OemPlus:
                this.viewport?.ZoomAt(this.Scale * 1.25, this.Center);
                break;
            case Key.Subtract or Key.OemMinus:
                this.viewport?.ZoomAt(this.Scale / 1.25, this.Center);
                break;
            default:
                return false;
        }

        return true;
    }

    private bool HandlePanKey(Key key)
    {
        var delta = key switch
        {
            Key.Left => new Vector(48, 0),
            Key.Right => new Vector(-48, 0),
            Key.Up => new Vector(0, 48),
            Key.Down => new Vector(0, -48),
            _ => default,
        };
        if (delta == default)
        {
            return false;
        }

        this.viewport?.Pan(delta);
        return true;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(this.Bounds.Size));
        if (this.image is not null && this.viewport is not null)
        {
            context.DrawImage(this.image, new Rect(this.image.Size), this.ImageBounds);
        }
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        this.viewport?.Resize(e.NewSize);
        this.Changed();
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        this.viewport?.ZoomAt(this.Scale * Math.Pow(1.2, e.Delta.Y), e.GetPosition(this));
        this.Changed();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.Focus();
            this.dragPosition = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (this.dragPosition is { } previous)
        {
            var position = e.GetPosition(this);
            this.viewport?.Pan(position - previous);
            this.dragPosition = position;
            this.Changed();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        this.dragPosition = null;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        this.dragPosition = null;
    }

    private Point Center => new(this.Bounds.Width / 2, this.Bounds.Height / 2);

    private void Changed()
    {
        this.InvalidateVisual();
        this.ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
