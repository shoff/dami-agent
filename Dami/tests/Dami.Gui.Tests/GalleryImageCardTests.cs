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

    [Fact]
    public void Should_Map_What_The_Curator_Saw_When_Present()
    {
        using var document = JsonDocument.Parse("""
            {"fileName":"dami.png","createdAt":"2026-09-01T12:00:00Z","prompt":"coffee","model":"gpt-image-2","isCanonical":false,
             "caption":"She sips coffee in a sunlit kitchen.","tags":["kitchen","coffee"],"source":"discord","score":0.91}
            """);

        var item = GalleryImageCard.From(document.RootElement);

        Assert.Equal("She sips coffee in a sunlit kitchen.", item.Description);
        Assert.Equal("kitchen · coffee", item.TagLine);
        Assert.Equal("discord", item.Source);
    }

    [Fact]
    public void Should_Fall_Back_To_The_Prompt_Before_The_Curator_Has_Looked()
    {
        using var document = JsonDocument.Parse("""
            {"fileName":"dami.png","createdAt":"2026-09-01T12:00:00Z","prompt":"coffee","model":"gpt-image-2","isCanonical":false,"caption":null,"tags":[],"source":"unknown"}
            """);

        var item = GalleryImageCard.From(document.RootElement);

        Assert.Equal("coffee", item.Description);
        Assert.Equal(string.Empty, item.TagLine);
    }
}
