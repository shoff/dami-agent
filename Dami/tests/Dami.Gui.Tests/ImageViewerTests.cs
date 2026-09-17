using Avalonia.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ImageViewerTests
{
    [Theory]
    [InlineData(Key.Left, -352, -300)]
    [InlineData(Key.Right, -448, -300)]
    [InlineData(Key.Up, -400, -252)]
    [InlineData(Key.Down, -400, -348)]
    public async Task HandleKey_Should_Pan_To_Details_Without_A_Mouse(Key key, double x, double y)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var viewer = new ImageViewer();
            viewer.Measure(new Size(800, 600));
            viewer.Arrange(new Rect(0, 0, 800, 600));
            viewer.ShowImage(new DrawingImage(), new Size(1600, 1200));
            viewer.HandleKey(Key.D1);
            viewer.HandleKey(key);

            Assert.Equal(new Point(x, y), viewer.ImageBounds.Position);
        });
    }

    [Theory]
    [InlineData(Key.D0, 0.5)]
    [InlineData(Key.D1, 1)]
    [InlineData(Key.Add, 0.625)]
    [InlineData(Key.Subtract, 0.4)]
    public async Task HandleKey_Should_Apply_The_Viewer_Action_To_The_Displayed_Image(Key key, double expected)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var viewer = new ImageViewer();
            viewer.Measure(new Size(800, 600));
            viewer.Arrange(new Rect(0, 0, 800, 600));
            var picture = new DrawingImage(new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(0, 0, 1600, 1200)),
            });
            viewer.ShowImage(picture, new Size(1600, 1200));
            viewer.HandleKey(key);

            Assert.Equal(expected, viewer.Scale);
        });
    }
}
