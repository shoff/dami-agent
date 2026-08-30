using Xunit;

namespace Dami.Gateway.Discord.Tests;

public sealed class DiscordSessionTests
{
    private static GatewayFrame Frame(string json) => DiscordGatewayProtocol.ReadFrame(json)!;

    [Fact]
    public void A_Fresh_Session_Cannot_Resume()
    {
        Assert.False(new DiscordSession().CanResume);
    }

    [Fact]
    public void Observe_Should_Capture_The_Session_Id_And_Self_Id_From_Ready()
    {
        var session = new DiscordSession();

        session.Observe(Frame(
            """{"op":0,"s":1,"t":"READY","d":{"session_id":"abc","user":{"id":"999"}}}"""));

        Assert.Equal("abc", session.SessionId);
        Assert.Equal("999", session.SelfId);
        Assert.True(session.CanResume);
    }

    [Fact]
    public void Observe_Should_Track_The_Latest_Sequence()
    {
        // RESUME replays from this number. Tracking it wrongly loses or duplicates
        // everything sent during a reconnect, and neither is visible from outside.
        var session = new DiscordSession();

        session.Observe(Frame("""{"op":0,"s":1,"t":"READY","d":{"session_id":"a","user":{"id":"1"}}}"""));
        session.Observe(Frame("""{"op":0,"s":7,"t":"MESSAGE_CREATE","d":{}}"""));

        Assert.Equal(7, session.LastSequence);
    }

    [Fact]
    public void Observe_Should_Keep_The_Last_Sequence_When_A_Frame_Carries_None()
    {
        // Heartbeat acks have no sequence. Letting them clear it would send RESUME back
        // to the beginning of the session.
        var session = new DiscordSession();

        session.Observe(Frame("""{"op":0,"s":4,"t":"READY","d":{"session_id":"a","user":{"id":"1"}}}"""));
        session.Observe(Frame("""{"op":11}"""));

        Assert.Equal(4, session.LastSequence);
    }

    [Fact]
    public void Invalidate_Should_Force_A_Fresh_Identify()
    {
        var session = new DiscordSession();
        session.Observe(Frame("""{"op":0,"s":2,"t":"READY","d":{"session_id":"a","user":{"id":"1"}}}"""));

        session.Invalidate();

        Assert.False(session.CanResume);
        Assert.Equal("1", session.SelfId);
    }

    [Fact]
    public void Observe_Should_Ignore_A_Ready_With_No_Session_Id()
    {
        var session = new DiscordSession();

        session.Observe(Frame("""{"op":0,"s":1,"t":"READY","d":{}}"""));

        Assert.False(session.CanResume);
    }
}

public sealed class DiscordRestTruncationTests
{
    [Fact]
    public void Truncate_Should_Leave_A_Short_Message_Alone()
    {
        Assert.Equal("hello", DiscordRest.Truncate("hello"));
    }

    [Fact]
    public void Truncate_Should_Say_That_It_Truncated()
    {
        // Discord refuses anything past 2000 characters. Silently cutting would make a
        // half-answer look like a whole one.
        var long_answer = new string('x', 2500);

        var cut = DiscordRest.Truncate(long_answer);

        Assert.Equal(2000, cut.Length);
        Assert.EndsWith("(truncated)", cut, StringComparison.Ordinal);
    }
}
