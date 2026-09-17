using Xunit;

namespace Dami.Gui.Tests;

public sealed class ChatScrollFollowTests
{
    [Fact]
    public void Observe_Should_Hold_A_Search_Result_Even_When_The_Conversation_Fits()
    {
        var follow = new ChatScrollFollow();
        follow.Pause();
        follow.Observe(0, 200, 400, false, true);

        Assert.False(follow.IsFollowing);
    }

    [Fact]
    public void Pause_Should_Keep_A_Search_Result_In_View_When_Content_Grows()
    {
        var follow = new ChatScrollFollow();
        follow.Pause();
        follow.Observe(0, 900, 400, true);

        Assert.False(follow.IsFollowing);
    }

    [Fact]
    public void Observe_Should_Pause_When_Reading_Earlier_Messages()
    {
        var follow = new ChatScrollFollow();

        follow.Observe(100, 1000, 400, false);

        Assert.False(follow.IsFollowing);
    }
    [Fact]
    public void Observe_Should_Keep_Following_When_Content_Reflows()
    {
        var follow = new ChatScrollFollow();

        follow.Observe(600, 1400, 400, true);

        Assert.True(follow.IsFollowing);
    }
    [Theory]
    [InlineData(568, true)]
    [InlineData(599, true)]
    [InlineData(567, false)]
    public void Observe_Should_Resume_Only_Near_The_Latest_Message(double offset, bool expected)
    {
        var follow = new ChatScrollFollow();
        follow.Observe(100, 1000, 400, false);

        follow.Observe(offset, 1000, 400, false);

        Assert.Equal(expected, follow.IsFollowing);
    }
    [Fact]
    public void Resume_Should_Follow_After_The_Reader_Pauses()
    {
        var follow = new ChatScrollFollow();
        follow.Observe(100, 1000, 400, false);

        follow.Resume();

        Assert.True(follow.IsFollowing);
    }
    [Theory]
    [InlineData(100, 1400, 400)]
    [InlineData(0, 300, 400)]
    [InlineData(100, 1000, 250)]
    public void Observe_Should_Keep_A_Paused_Reader_Paused_During_Layout_Changes(
        double offset, double extent, double viewport)
    {
        var follow = new ChatScrollFollow();
        follow.Observe(100, 1000, 400, false);

        follow.Observe(offset, extent, viewport, true);

        Assert.False(follow.IsFollowing);
    }

    [Fact]
    public void Observe_Should_Follow_When_All_Content_Fits()
    {
        var follow = new ChatScrollFollow();

        follow.Observe(0, 200, 400, false);

        Assert.True(follow.IsFollowing);
    }

    [Fact]
    public void Constructor_Should_Follow_New_Messages()
    {
        Assert.True(new ChatScrollFollow().IsFollowing);
    }
}
