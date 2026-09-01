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
        Assert.Equal("what is this", DiscordPrompt.Question(Message("what is this"), []));
    }

    [Fact]
    public void Question_Should_Fold_A_Caption_In_As_Context()
    {
        var question = DiscordPrompt.Question(
            Message("what is this", Image("bolt.png")), ["a rusted hex bolt on a workbench"]);

        Assert.Contains("a rusted hex bolt on a workbench", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Question_Should_Keep_The_Askers_Words_Alongside_The_Caption()
    {
        var question = DiscordPrompt.Question(
            Message("what size is this", Image("bolt.png")), ["a rusted hex bolt"]);

        Assert.Contains("what size is this", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Question_Should_Stand_Alone_When_An_Image_Arrives_With_No_Words()
    {
        // Sending a photo with no caption is a question — "what am I looking at" — and an
        // empty prompt would be rejected before it ever reached a model.
        var question = DiscordPrompt.Question(Message(string.Empty, Image("bolt.png")), ["a hex bolt"]);

        Assert.NotEmpty(question.Trim());
    }

    [Fact]
    public void Exchanges_Should_Read_Oldest_First_As_Labelled_Lines()
    {
        var lines = DiscordPrompt.Exchanges(
            [("what did I lift monday", "225 for five"), ("and tuesday", "you rested")]);

        Assert.Equal(
            ["Earlier — Steve: what did I lift monday", "Earlier — Dami: 225 for five",
             "Earlier — Steve: and tuesday", "Earlier — Dami: you rested"],
            lines);
    }

    [Fact]
    public void Exchanges_Should_Be_Empty_For_A_First_Message()
    {
        Assert.Empty(DiscordPrompt.Exchanges([]));
    }
}
