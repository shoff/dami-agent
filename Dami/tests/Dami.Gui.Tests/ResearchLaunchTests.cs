using Xunit;

namespace Dami.Gui.Tests;

public sealed class ResearchLaunchTests
{
    [Fact]
    public void Prepare_Should_Let_The_Frontier_Choose_Sites_When_The_Starting_Url_Is_Empty()
    {
        var prepared = ResearchLaunch.Prepare("  ", "Find public evidence", new ChatDraft("", []), false);

        Assert.Contains("seedUrls", prepared.Prompt ?? "", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://public.example/data", "Find data", "", false, false, true)]
    [InlineData("https://public.example/data", "Find data", "My draft", false, false, false)]
    [InlineData("https://public.example/data", "Find data", "", true, false, false)]
    [InlineData("https://public.example/data", "Find data", "", false, true, false)]
    [InlineData("file:///etc/passwd", "Find data", "", false, false, false)]
    [InlineData("https://user:pass@public.example", "Find data", "", false, false, false)]
    [InlineData("not a URL", "Find data", "", false, false, false)]
    [InlineData("https://public.example/data", "   ", "", false, false, false)]
    public void Prepare_Should_Validate_Research_And_Preserve_Existing_Work(
        string seed, string question, string text, bool image, bool busy, bool ready)
    {
        var draft = new ChatDraft(text, image ? [new DirectChatImage("draft.png", "image/png", [1])] : []);
        var prepared = ResearchLaunch.Prepare(seed, question, draft, busy);
        Assert.Equal(ready, prepared.Prompt is not null);
        Assert.False(string.IsNullOrWhiteSpace(prepared.Message));
        Assert.Equal((text, image ? 1 : 0), (draft.Text, draft.Images.Count));
        if (ready)
        {
            Assert.Contains("deep_research", prepared.Prompt!, StringComparison.Ordinal);
            Assert.Contains(seed, prepared.Prompt!, StringComparison.Ordinal);
            Assert.Contains(question, prepared.Prompt!, StringComparison.Ordinal);
        }
    }
}
