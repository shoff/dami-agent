using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordAnswerTests
{
    [Fact]
    public void A_Refusal_Should_Itself_Be_Sendable()
    {
        // Silence would be the wrong failure: he asked a question and is owed the reason.
        var trace = Guid.NewGuid();

        var refusal = DiscordAnswer.Refusal("chan-1", trace);

        Assert.Equal(ContentProvenance.Operational, refusal.Provenance);
        Assert.Contains(trace.ToString(), refusal.Text, StringComparison.Ordinal);
        Assert.Contains("ADR-0025", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Refusal_Should_Not_Repeat_The_Answer_It_Refused()
    {
        // The obvious bug in a refusal path is quoting what it would not send.
        var refusal = DiscordAnswer.Refusal("chan-1", Guid.NewGuid());

        Assert.DoesNotContain("an answer", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Frontier_Failure_Should_Be_Said_Not_Answered_Around()
    {
        // ADR-0028: the only alternative to the frontier's answer is the reason there is none.
        var trace = Guid.NewGuid();

        var message = DiscordAnswer.FrontierUnavailable("chan-1", trace, "codex down");

        Assert.Equal(ContentProvenance.Operational, message.Provenance);
        Assert.Contains("codex down", message.Text, StringComparison.Ordinal);
        Assert.Contains(trace.ToString(), message.Text, StringComparison.Ordinal);
        Assert.Contains("Nothing was answered locally", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Frontier_Failure_Should_Keep_The_Reason_To_One_Short_Line()
    {
        // Exception messages arrive with stack-shaped line breaks and no length limit;
        // Discord gets one sentence, not a log excerpt.
        var reason = "line one\nline two " + new string('x', 400);

        var message = DiscordAnswer.FrontierUnavailable("chan-1", Guid.Empty, reason);

        Assert.DoesNotContain('\n', message.Text);
        Assert.True(message.Text.Length < 400, $"{message.Text.Length} characters");
    }
}
