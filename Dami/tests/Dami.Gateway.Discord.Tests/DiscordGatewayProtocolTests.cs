using System.Text.Json;
using Xunit;

namespace Dami.Gateway.Discord.Tests;

public sealed class DiscordGatewayProtocolTests
{
    [Fact]
    public void Intents_Should_Request_Message_Content()
    {
        // Without the privileged message content intent the socket connects, events
        // arrive, and every body is empty - a failure that looks like silence. Asserting
        // the bit means a refactor cannot quietly drop it.
        const int messageContent = 1 << 15;

        Assert.Equal(messageContent, DiscordGatewayProtocol.INTENTS & messageContent);
    }

    [Fact]
    public void Intents_Should_Not_Request_Anything_Privileged_Beyond_Message_Content()
    {
        // GUILD_MEMBERS and GUILD_PRESENCES are the other privileged intents. Asking for
        // them would require justification to Discord and give the bot reach it does not
        // need.
        const int members = 1 << 1;
        const int presences = 1 << 8;

        Assert.Equal(0, DiscordGatewayProtocol.INTENTS & (members | presences));
    }

    [Fact]
    public void ReadFrame_Should_Read_Opcode_Sequence_And_Name()
    {
        var frame = DiscordGatewayProtocol.ReadFrame(
            """{"op":0,"s":42,"t":"MESSAGE_CREATE","d":{"content":"hi"}}""");

        Assert.NotNull(frame);
        Assert.Equal(DiscordOpcode.Dispatch, frame.Opcode);
        Assert.Equal(42, frame.Sequence);
        Assert.Equal("MESSAGE_CREATE", frame.EventName);
    }

    [Fact]
    public void ReadFrame_Should_Tolerate_A_Frame_With_No_Sequence_Or_Name()
    {
        // HELLO carries neither, and treating that as malformed would break every
        // connection at the first frame.
        var frame = DiscordGatewayProtocol.ReadFrame("""{"op":10,"d":{"heartbeat_interval":41250}}""");

        Assert.NotNull(frame);
        Assert.Null(frame.Sequence);
        Assert.Null(frame.EventName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"no_op":1}""")]
    [InlineData("""{"op":"not a number"}""")]
    public void ReadFrame_Should_Return_Null_For_Anything_That_Is_Not_A_Frame(string json)
    {
        // A garbage frame must not kill the connection loop; the socket is a hostile
        // input like any other.
        Assert.Null(DiscordGatewayProtocol.ReadFrame(json));
    }

    [Fact]
    public void ReadHeartbeatInterval_Should_Read_Hello()
    {
        var frame = DiscordGatewayProtocol.ReadFrame("""{"op":10,"d":{"heartbeat_interval":41250}}""")!;

        Assert.Equal(TimeSpan.FromMilliseconds(41250), DiscordGatewayProtocol.ReadHeartbeatInterval(frame));
    }

    [Fact]
    public void ReadHeartbeatInterval_Should_Refuse_A_Non_Hello_Frame()
    {
        var frame = DiscordGatewayProtocol.ReadFrame("""{"op":11}""")!;

        Assert.Null(DiscordGatewayProtocol.ReadHeartbeatInterval(frame));
    }

    [Fact]
    public void ReadHeartbeatInterval_Should_Refuse_A_Zero_Interval()
    {
        // A zero interval would spin the heartbeat loop against Discord, which reads as
        // an attack rather than a bug.
        var frame = DiscordGatewayProtocol.ReadFrame("""{"op":10,"d":{"heartbeat_interval":0}}""")!;

        Assert.Null(DiscordGatewayProtocol.ReadHeartbeatInterval(frame));
    }

    [Fact]
    public void ReadMessage_Should_Read_A_Message_Create()
    {
        var frame = DiscordGatewayProtocol.ReadFrame(
            """
            {"op":0,"s":3,"t":"MESSAGE_CREATE","d":{
              "id":"111","channel_id":"222","guild_id":"333",
              "content":"what is on the board?",
              "author":{"id":"444","bot":false}}}
            """)!;

        var message = DiscordGatewayProtocol.ReadMessage(frame);

        Assert.NotNull(message);
        Assert.Equal("222", message.ChannelId);
        Assert.Equal("333", message.GuildId);
        Assert.Equal("444", message.AuthorId);
        Assert.False(message.AuthorIsBot);
        Assert.Equal("what is on the board?", message.Content);
    }

    [Fact]
    public void ReadMessage_Should_Mark_A_Bot_Author()
    {
        // The gateway must recognise its own echo, or it answers itself forever.
        var frame = DiscordGatewayProtocol.ReadFrame(
            """{"op":0,"t":"MESSAGE_CREATE","d":{"content":"x","author":{"id":"9","bot":true}}}""")!;

        Assert.True(DiscordGatewayProtocol.ReadMessage(frame)!.AuthorIsBot);
    }

    [Fact]
    public void ReadMessage_Should_Return_Null_For_Another_Dispatch()
    {
        var frame = DiscordGatewayProtocol.ReadFrame("""{"op":0,"t":"TYPING_START","d":{}}""")!;

        Assert.Null(DiscordGatewayProtocol.ReadMessage(frame));
    }

    [Fact]
    public void ReadMessage_Should_Survive_A_Direct_Message_With_No_Guild()
    {
        // A DM has no guild_id at all. Treating the absence as malformed would make the
        // bot deaf in exactly the place Steve is most likely to talk to it.
        var frame = DiscordGatewayProtocol.ReadFrame(
            """{"op":0,"t":"MESSAGE_CREATE","d":{"channel_id":"5","content":"hi","author":{"id":"7"}}}""")!;

        var message = DiscordGatewayProtocol.ReadMessage(frame);

        Assert.NotNull(message);
        Assert.Equal(string.Empty, message.GuildId);
        Assert.False(message.AuthorIsBot);
    }

    [Fact]
    public void Identify_Should_Carry_The_Token_And_The_Intents()
    {
        using var document = JsonDocument.Parse(DiscordGatewayProtocol.Identify("a-token"));
        var data = document.RootElement.GetProperty("d");

        Assert.Equal((int)DiscordOpcode.Identify, document.RootElement.GetProperty("op").GetInt32());
        Assert.Equal("a-token", data.GetProperty("token").GetString());
        Assert.Equal(DiscordGatewayProtocol.INTENTS, data.GetProperty("intents").GetInt32());
    }

    [Fact]
    public void Identify_Should_Refuse_An_Empty_Token()
    {
        Assert.Throws<ArgumentException>(() => DiscordGatewayProtocol.Identify("  "));
    }

    [Fact]
    public void Heartbeat_Should_Carry_Null_Before_Any_Sequence_Is_Seen()
    {
        using var document = JsonDocument.Parse(DiscordGatewayProtocol.Heartbeat(null));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("d").ValueKind);
    }

    [Fact]
    public void Resume_Should_Carry_The_Session_And_Sequence()
    {
        using var document = JsonDocument.Parse(DiscordGatewayProtocol.Resume("t", "session-1", 17));
        var data = document.RootElement.GetProperty("d");

        Assert.Equal((int)DiscordOpcode.Resume, document.RootElement.GetProperty("op").GetInt32());
        Assert.Equal("session-1", data.GetProperty("session_id").GetString());
        Assert.Equal(17, data.GetProperty("seq").GetInt32());
    }
}
