using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Gateway.Discord.Tests;

public sealed class DiscordEgressChannelTests
{
    private const string OWNER = "347544641295613953";
    private const string SELF = "999000111";
    private const string GUILD = "1465847432570077402";

    private const string HELLO = """{"op":10,"d":{"heartbeat_interval":45000}}""";

    private static readonly string ready =
        "{\"op\":0,\"s\":1,\"t\":\"READY\",\"d\":{\"session_id\":\"sess-1\","
        + "\"user\":{\"id\":\"" + SELF + "\"}}}";

    private static readonly string readyWithResumeUrl =
        "{\"op\":0,\"s\":1,\"t\":\"READY\",\"d\":{\"session_id\":\"sess-1\","
        + "\"resume_gateway_url\":\"wss://resume.example.discord.gg\","
        + "\"user\":{\"id\":\"" + SELF + "\"}}}";

    private static string Message(string authorId, string text, string guildId = GUILD) =>
        "{\"op\":0,\"s\":2,\"t\":\"MESSAGE_CREATE\",\"d\":{\"id\":\"1\","
        + "\"channel_id\":\"chan-1\",\"guild_id\":\"" + guildId + "\","
        + "\"content\":\"" + text + "\","
        + "\"author\":{\"id\":\"" + authorId + "\",\"bot\":false}}}";

    /// <summary>A socket that replays a fixed script, then reports the peer closed it.</summary>
    private sealed class ScriptedSocket : IDiscordSocket
    {
        private readonly Queue<string> frames;

        public ScriptedSocket(params string[] frames) => this.frames = new Queue<string>(frames);

        public List<string> Sent { get; } = [];

        public Uri? ConnectedTo { get; private set; }

        public DiscordClose? CloseReason { get; set; }

        public Task ConnectAsync(Uri gateway, CancellationToken cancellationToken)
        {
            this.ConnectedTo = gateway;
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken cancellationToken)
        {
            this.Sent.Add(json);
            return Task.CompletedTask;
        }

        public Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(this.frames.Count > 0 ? this.frames.Dequeue() : null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DiscordOptions Options() => new()
    {
        Token = "a-token",
        OwnerUserId = OWNER,
        GuildId = GUILD,
        Enabled = true,
    };

    private static DiscordEgressChannel Channel(IDiscordSocket socket, IDiscordRest? rest = null) =>
        Channel(() => socket, rest);

    private static DiscordEgressChannel Channel(Func<IDiscordSocket> connect, IDiscordRest? rest = null) =>
        new(
            connect,
            rest ?? Substitute.For<IDiscordRest>(),
            Options(),
            TimeProvider.System,
            NullLogger<DiscordEgressChannel>.Instance);

    /// <summary>Listens until one message arrives or the window closes.</summary>
    private static async Task<List<InboundMessage>> HeardAsync(
        DiscordEgressChannel channel, int expected, TimeSpan window)
    {
        using var cancellation = new CancellationTokenSource(window);
        var heard = new List<InboundMessage>();

        try
        {
            await foreach (var message in channel.ListenAsync(cancellation.Token))
            {
                heard.Add(message);
                if (heard.Count >= expected)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window closing is how "nothing more arrived" is expressed.
        }

        return heard;
    }

    [Fact]
    public async Task ListenAsync_Should_Identify_On_A_Fresh_Connection()
    {
        var socket = new ScriptedSocket(HELLO, ready, Message(OWNER, "hello"));

        await HeardAsync(Channel(socket), 1, TimeSpan.FromSeconds(5));

        Assert.Contains(socket.Sent, sent => sent.Contains("\"op\":2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListenAsync_Should_Yield_A_Message_From_The_Owner()
    {
        var socket = new ScriptedSocket(HELLO, ready, Message(OWNER, "what is on the board?"));

        var heard = await HeardAsync(Channel(socket), 1, TimeSpan.FromSeconds(5));

        Assert.Single(heard);
        Assert.Equal("what is on the board?", heard[0].Text);
        Assert.Equal("chan-1", heard[0].ConversationId);
    }

    [Fact]
    public async Task ListenAsync_Should_Ignore_Everyone_Who_Is_Not_The_Owner()
    {
        // A bot in a server is addressable by every member of it. Without this the
        // runtime takes instructions from strangers.
        var socket = new ScriptedSocket(HELLO, ready, Message("111222333", "delete everything"));

        var heard = await HeardAsync(Channel(socket), 1, TimeSpan.FromSeconds(2));

        Assert.Empty(heard);
    }

    [Fact]
    public async Task ListenAsync_Should_Ignore_Its_Own_Messages()
    {
        // Otherwise the gateway answers itself, and each answer is another message.
        var socket = new ScriptedSocket(HELLO, ready, Message(SELF, "an answer"));

        var heard = await HeardAsync(Channel(socket), 1, TimeSpan.FromSeconds(2));

        Assert.Empty(heard);
    }

    [Fact]
    public async Task ListenAsync_Should_Ignore_A_Message_From_Another_Guild()
    {
        var socket = new ScriptedSocket(HELLO, ready, Message(OWNER, "hi", guildId: "8888"));

        var heard = await HeardAsync(Channel(socket), 1, TimeSpan.FromSeconds(2));

        Assert.Empty(heard);
    }

    [Fact]
    public async Task ListenAsync_Should_Resume_Rather_Than_Reidentify_After_A_Drop()
    {
        // The first socket ends after READY, which forces a reconnect onto the second.
        // A gateway that identifies again instead of resuming looks healthy while losing
        // every message sent during the gap.
        var first = new ScriptedSocket(HELLO, ready);
        var second = new ScriptedSocket(HELLO);
        var sockets = new Queue<IDiscordSocket>([first, second]);

        await HeardAsync(
            Channel(() => sockets.Count > 0 ? sockets.Dequeue() : new ScriptedSocket()),
            1,
            TimeSpan.FromSeconds(4));

        Assert.Contains(first.Sent, sent => sent.Contains("\"op\":2", StringComparison.Ordinal));
        Assert.Contains(second.Sent, sent => sent.Contains("\"op\":6", StringComparison.Ordinal));
        Assert.Contains(second.Sent, sent => sent.Contains("sess-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_Should_Carry_Profile_Derived_Content_To_Its_Own_Subject()
    {
        // ADR-0025. This channel only ever replies into Steve's own conversation, because
        // ShouldAnswer drops every inbound message that is not his. Refusing here was what
        // made the gateway answer "hi there" by quoting a decision record at him.
        var rest = Substitute.For<IDiscordRest>();
        var channel = Channel(new ScriptedSocket(), rest);
        var content = new OutboundContent(
            "chan-1", "You were in Chicago on Tuesday", ContentProvenance.ProfileDerived, Guid.NewGuid());

        await channel.SendAsync(content, CancellationToken.None);

        await rest.Received(1).PostMessageAsync(
            "chan-1", "You were in Chicago on Tuesday", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_Should_Post_Operational_Content()
    {
        var rest = Substitute.For<IDiscordRest>();
        var channel = Channel(new ScriptedSocket(), rest);
        var content = new OutboundContent(
            "chan-1", "3 tasks open on the board", ContentProvenance.Operational, Guid.NewGuid());

        await channel.SendAsync(content, CancellationToken.None);

        await rest.Received(1).PostMessageAsync(
            "chan-1", "3 tasks open on the board", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListenAsync_Should_Stop_On_A_Fatal_Close_Rather_Than_Retry()
    {
        // Discord resets the token of a bot that burns its identify budget. On 2026-08-30
        // this loop ran at 24 identifies a minute against a limit of one per five seconds,
        // because a permanent refusal was indistinguishable from a dropped connection.
        var attempts = 0;
        IDiscordSocket Refusing()
        {
            attempts++;
            return new ScriptedSocket { CloseReason = new DiscordClose(4014, "Disallowed intent(s)") };
        }

        await HeardAsync(Channel(Refusing), 1, TimeSpan.FromSeconds(3));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ListenAsync_Should_Keep_Retrying_A_Non_Fatal_Close()
    {
        // The opposite failure: giving up on an ordinary drop would make the gateway
        // fragile rather than safe.
        var attempts = 0;
        IDiscordSocket Dropping()
        {
            attempts++;
            return new ScriptedSocket { CloseReason = new DiscordClose(1006, "abnormal closure") };
        }

        await HeardAsync(Channel(Dropping), 1, TimeSpan.FromSeconds(3));

        Assert.True(attempts > 1, $"expected more than one attempt, got {attempts}");
    }

    [Fact]
    public async Task ListenAsync_Should_Resume_To_The_Url_Discord_Named()
    {
        // Resuming against the generic gateway is answered with INVALID_SESSION, which
        // presents as an endless identify loop rather than as an error.
        var first = new ScriptedSocket(HELLO, readyWithResumeUrl);
        var second = new ScriptedSocket(HELLO);
        var sockets = new Queue<IDiscordSocket>([first, second]);

        await HeardAsync(
            Channel(() => sockets.Count > 0 ? sockets.Dequeue() : new ScriptedSocket()),
            1,
            TimeSpan.FromSeconds(4));

        Assert.Equal(new Uri("wss://resume.example.discord.gg"), second.ConnectedTo);
    }

    [Fact]
    public void A_Fatal_Close_Should_Explain_Itself()
    {
        Assert.True(new DiscordClose(4004, "Authentication failed").IsFatal);
        Assert.Contains("token", new DiscordClose(4004, "x").Advice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MESSAGE CONTENT", new DiscordClose(4014, "x").Advice, StringComparison.Ordinal);
        Assert.False(new DiscordClose(1006, "abnormal closure").IsFatal);
    }
}
