using Avalonia;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ImageViewportTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(800, 0)]
    [InlineData(double.PositiveInfinity, 600)]
    [InlineData(double.NaN, 600)]
    public void Resize_Should_Ignore_An_Unmeasured_Or_Invalid_Viewport(double width, double height)
    {
        var view = new ImageViewport(new Size(1600, 1200));
        view.Fit(new Size(800, 600));
        view.Resize(new Size(width, height));

        Assert.Equal(new Rect(0, 0, 800, 600), view.ImageBounds);
    }

    [Theory]
    [InlineData(false, 0.25)]
    [InlineData(true, 1)]
    public void Resize_Should_Refit_Only_When_The_User_Has_Not_Zoomed(bool zoomed, double expected)
    {
        var view = new ImageViewport(new Size(1600, 1200));
        view.Fit(new Size(800, 600));
        if (zoomed)
        {
            view.ZoomAt(1, new Point(400, 300));
        }

        view.Resize(new Size(400, 300));

        Assert.Equal(expected, view.Scale);
    }

    [Theory]
    [InlineData(100, 8)]
    [InlineData(0.001, 0.05)]
    [InlineData(0, 0.05)]
    public void ZoomAt_Should_Clamp_To_Usable_Scale_Limits(double requested, double expected)
    {
        var view = new ImageViewport(new Size(1600, 1200));
        view.Fit(new Size(800, 600));
        view.ZoomAt(requested, new Point(400, 300));

        Assert.Equal(expected, view.Scale);
    }

    [Theory]
    [InlineData(1, 2000, 1000, 0, 0)]
    [InlineData(1, -2000, -1000, -800, -600)]
    [InlineData(0.25, 2000, -1000, 200, 150)]
    public void Pan_Should_Keep_Image_Edges_Inside_The_Viewport(double scale, double x, double y, double left, double top)
    {
        var view = new ImageViewport(new Size(1600, 1200));
        view.Fit(new Size(800, 600));
        view.ZoomAt(scale, new Point(400, 300));
        view.Pan(new Vector(x, y));

        Assert.Equal(new Point(left, top), view.ImageBounds.Position);
    }

    [Fact]
    public void ZoomAt_Should_Keep_The_Pixel_Under_The_Pointer()
    {
        var view = new ImageViewport(new Size(1600, 1200));
        view.Fit(new Size(800, 600));
        view.ZoomAt(1, new Point(200, 150));

        Assert.Equal(new Rect(-200, -150, 1600, 1200), view.ImageBounds);
    }

    [Theory]
    [InlineData(1600, 900, 800, 600, 0.5)]
    [InlineData(900, 1600, 800, 600, 0.375)]
    [InlineData(200, 100, 800, 600, 1)]
    public void Fit_Should_Show_The_Whole_Image_Without_Enlarging_Small_Images(
        double width, double height, double viewportWidth, double viewportHeight, double expected)
    {
        var view = new ImageViewport(new Size(width, height));
        view.Fit(new Size(viewportWidth, viewportHeight));

        Assert.Equal(expected, view.Scale);
    }
}
