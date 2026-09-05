using Dami.Proactive.Gallery;
using Xunit;

namespace Dami.Proactive.Tests.Gallery;

public sealed class GalleryCaptionTests
{
    [Fact]
    public void Json_Reply_Yields_Caption_And_Lowercased_Distinct_Tags()
    {
        var (caption, tags) = GalleryCaption.Parse(
            """Sure! {"caption":"She sips coffee in a sunlit kitchen.","tags":["Kitchen","coffee","kitchen","  Morning "]}""");

        Assert.Equal("She sips coffee in a sunlit kitchen.", caption);
        Assert.Equal(["kitchen", "coffee", "morning"], tags);
    }

    [Fact]
    public void Prose_Reply_Becomes_The_Caption_With_No_Tags()
    {
        // Small local models answer in prose however firmly they are asked for JSON.
        var (caption, tags) = GalleryCaption.Parse("A woman reading on a sofa at night.");

        Assert.Equal("A woman reading on a sofa at night.", caption);
        Assert.Empty(tags);
    }

    [Fact]
    public void Broken_Json_Falls_Back_To_Prose_Rather_Than_Nothing()
    {
        var (caption, _) = GalleryCaption.Parse("""{"caption": "unterminated""");

        Assert.Contains("unterminated", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Tags_Are_Capped_At_Twelve()
    {
        var many = string.Join(",", Enumerable.Range(0, 20).Select(i => $"\"t{i}\""));

        var (_, tags) = GalleryCaption.Parse($$"""{"caption":"c","tags":[{{many}}]}""");

        Assert.Equal(12, tags.Count);
    }

    [Fact]
    public void Embeddable_Text_Carries_Caption_Tags_And_Prompt()
    {
        var text = GalleryCaption.Embeddable("c", ["a", "b"], "p");

        Assert.Equal("c\nTags: a, b\nPrompt: p", text);
        Assert.Equal("c", GalleryCaption.Embeddable("c", [], string.Empty));
    }
}
