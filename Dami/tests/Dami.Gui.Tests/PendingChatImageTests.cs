using Xunit;

namespace Dami.Gui.Tests;

public sealed class PendingChatImageTests
{
    [Fact]
    public void PreservesRequestMetadata()
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nCEAAAAASUVORK5CYII=");
        var request = new DirectChatImage("pixel.png", "image/png", bytes);

        var subject = new PendingChatImage(request);

        Assert.Same(request, subject.Request);
        Assert.Equal("pixel.png", subject.FileName);
    }
}
