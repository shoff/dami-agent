using System.Text.Json;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class GalleryImageCardTests
{
    [Fact]
    public void Should_Map_Gallery_Metadata()
    {
        using var document = JsonDocument.Parse("""
            {"fileName":"dami.png","createdAt":"2026-09-01T12:00:00Z","prompt":"coffee","model":"gpt-image-2","isCanonical":true}
            """);

        var item = GalleryImageCard.From(document.RootElement);

        Assert.Equal("dami.png", item.FileName);
        Assert.Equal("coffee", item.Prompt);
        Assert.Equal("identity anchor", item.Badge);
    }
}
