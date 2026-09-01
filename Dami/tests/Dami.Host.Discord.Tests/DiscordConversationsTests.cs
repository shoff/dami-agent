using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordConversationsTests
{
    [Fact]
    public void SessionFor_Should_Be_Stable_For_A_Channel()
    {
        // The mapping has to survive a restart without a table to look it up in, which
        // is the whole reason it is derived rather than stored.
        Assert.Equal(
            DiscordConversations.SessionFor("1234567890"),
            DiscordConversations.SessionFor("1234567890"));
    }

    [Fact]
    public void SessionFor_Should_Differ_Between_Channels()
    {
        Assert.NotEqual(
            DiscordConversations.SessionFor("1234567890"),
            DiscordConversations.SessionFor("9876543210"));
    }

    [Fact]
    public void SessionFor_Should_Never_Be_Empty()
    {
        // Guid.Empty is rejected by ConversationSession, so a channel id that hashed to
        // zero would fail at the store rather than here.
        Assert.NotEqual(Guid.Empty, DiscordConversations.SessionFor("0"));
    }
}
