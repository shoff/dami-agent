using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordPromptTests
{
    private static InboundMessage Message(string text, params InboundAttachment[] attachments) =>
        new("owner", "channel", text, DateTimeOffset.UnixEpoch) { Attachments = [.. attachments] };

    private static InboundAttachment Image(string name) =>
        new(name, $"https://cdn.discordapp.com/{name}", "image/png", 1024);

    [Fact]
    public void Question_Should_Be_The_Text_When_Nothing_Is_Attached()
    {
        Assert.Equal("what is this", DiscordPrompt.Question(Message("what is this")));
    }

    [Fact]
    public void Question_Should_Not_Carry_A_Caption()
    {
        // The whole point of the fix: a caption is locally derived from a LocalOnly image
        // (D-012), so it must go through the disclosure gate as context. The question is
        // appended to the frontier prompt ungated, so a caption in it would be a leak.
        Assert.Equal("what is this", DiscordPrompt.Question(Message("what is this", Image("bolt.png"))));
    }

    [Fact]
    public void LocalContext_Should_Carry_Captions_As_Gateable_Lines()
    {
        var context = DiscordPrompt.LocalContext([], ["a rusted hex bolt on a workbench"]);

        Assert.Contains(
            context,
            line => line.Contains("a rusted hex bolt on a workbench", StringComparison.Ordinal));
    }

    [Fact]
    public void LocalContext_Should_Label_A_Caption_As_Locally_Described()
    {
        // The gate classifies text it is given; telling it the line describes an image
        // Steve sent is the difference between judging a caption and judging a stray noun.
        var context = DiscordPrompt.LocalContext([], ["a lab report showing a value"]);

        Assert.StartsWith("Image Steve sent", Assert.Single(context), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalContext_Should_Put_Prior_Exchanges_Before_Captions()
    {
        var context = DiscordPrompt.LocalContext(
            [("what did I lift", "225")], ["a photo of a barbell"]);

        Assert.StartsWith("Earlier", context[0], StringComparison.Ordinal);
        Assert.StartsWith("Image", context[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Question_Should_Stand_Alone_When_An_Image_Arrives_With_No_Words()
    {
        // A photo with no caption is still a question, and an empty prompt would be
        // rejected before it reached a model.
        var question = DiscordPrompt.Question(Message(string.Empty, Image("bolt.png")));

        Assert.NotEmpty(question.Trim());
    }

    [Fact]
    public void Question_Should_Be_Empty_When_There_Is_Neither_Text_Nor_Image()
    {
        Assert.Empty(DiscordPrompt.Question(Message(string.Empty)));
    }

    [Fact]
    public void Exchanges_Should_Read_Oldest_First_As_Labelled_Lines()
    {
        var lines = DiscordPrompt.LocalContext(
            [("what did I lift monday", "225 for five"), ("and tuesday", "you rested")], []);

        Assert.Equal(
            ["Earlier — Steve: what did I lift monday", "Earlier — Dami: 225 for five",
             "Earlier — Steve: and tuesday", "Earlier — Dami: you rested"],
            lines);
    }

    [Fact]
    public void LocalContext_Should_Be_Empty_For_A_First_Plain_Message()
    {
        Assert.Empty(DiscordPrompt.LocalContext([], []));
    }
}
