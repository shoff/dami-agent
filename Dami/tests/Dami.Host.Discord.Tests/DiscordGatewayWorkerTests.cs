using Dami.Contracts.Context;
using Dami.Contracts.Gateways;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordGatewayWorkerTests
{
    private static DiscordOptions Configured(bool frontier = false) => new()
    {
        Token = "a-token",
        OwnerUserId = "347544641295613953",
        Enabled = true,
        Frontier = frontier,
    };

    private static async IAsyncEnumerable<InboundMessage> OneAsync(InboundMessage message)
    {
        yield return message;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ConversationTurn> NoTurnsAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static InboundMessage From(string text) =>
        new("owner", "chan-1", text, DateTimeOffset.UnixEpoch);

    private static TurnResult Answer(PrivacyClass privacy, bool withMemory) =>
        new(
            Guid.NewGuid(),
            "the answer text",
            new AssembledContext(
                withMemory
                    ? [new RetrievedItem("observation", Guid.NewGuid(), "private", DateTimeOffset.UnixEpoch)]
                    : [],
                [],
                10),
            new ModelRoute(ModelTier.Local, privacy, "because"));

    private static IGatewayAuthority Granting()
    {
        var lease = Substitute.For<IGatewayLease>();
        var authority = Substitute.For<IGatewayAuthority>();
        authority.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lease);
        return authority;
    }

    private static async Task RunAsync(DiscordGatewayWorker worker)
    {
        await worker.StartAsync(CancellationToken.None);
        if (worker.ExecuteTask is { } running)
        {
            await running;
        }

        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>Everything the worker needs, so one test can vary one thing.</summary>
    private sealed class Harness
    {
        public IGatewayAuthority Authority { get; set; } = Granting();

        public IEgressChannel Channel { get; init; } = Substitute.For<IEgressChannel>();

        public ITracedTurnRunner Turns { get; init; } = Substitute.For<ITracedTurnRunner>();

        public IAugmentedTurn Augmented { get; init; } = Substitute.For<IAugmentedTurn>();

        public IVisionClient Vision { get; init; } = Substitute.For<IVisionClient>();

        public IDiscordRest Rest { get; init; } = Substitute.For<IDiscordRest>();

        public IConversationSessionStore Sessions { get; init; } =
            Substitute.For<IConversationSessionStore>();

        /// <summary>Empty history by default; a test that wants some overrides it.</summary>
        public IConversationTurnStore TurnStore { get; init; } = EmptyHistory();

        public DiscordOptions Options { get; set; } = Configured();

        private static IConversationTurnStore EmptyHistory()
        {
            var store = Substitute.For<IConversationTurnStore>();
            store.RecentCompletedTurnsAsync(
                    Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(NoTurnsAsync());
            return store;
        }

        public DiscordGatewayWorker Build()
        {
            return new DiscordGatewayWorker(
                this.Authority,
                this.Channel,
                this.Turns,
                this.Augmented,
                new DiscordVision(this.Vision, this.Rest, NullLogger<DiscordVision>.Instance),
                this.Sessions,
                this.TurnStore,
                Substitute.For<IProactiveRunHistory>(),
                TimeProvider.System,
                this.Options,
                NullLogger<DiscordGatewayWorker>.Instance);
        }
    }

    private static Harness Listening(InboundMessage message)
    {
        var channel = Substitute.For<IEgressChannel>();
        channel.ListenAsync(Arg.Any<CancellationToken>()).Returns(OneAsync(message));
        return new Harness { Channel = channel };
    }

    private static void Answers(Harness harness, TurnResult result)
    {
        harness.Turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns(result);
    }

    [Fact]
    public async Task Should_Not_Serve_Without_Authority()
    {
        // Two bots on one token answer every message twice and neither can see the other.
        var authority = Substitute.For<IGatewayAuthority>();
        authority.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IGatewayLease?)null);
        var harness = new Harness { Authority = authority };

        await RunAsync(harness.Build());

        harness.Channel.DidNotReceive().ListenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Take_Authority_When_Unconfigured()
    {
        // An empty token must not cause the process to claim the gateway and lock out a
        // correctly configured one.
        var harness = new Harness { Options = new DiscordOptions() };

        await RunAsync(harness.Build());

        await harness.Authority.DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Answer_Status_From_Runtime_State_Without_A_Turn()
    {
        // This test used to stub the turn runner and assert the runner's answer came back,
        // so it passed whether or not the operational path fired — mutation testing found
        // it. What matters is that "status" is answered WITHOUT assembling context, since
        // doing so would retrieve Steve's memories in order to report service health.
        var harness = Listening(From("status"));
        Answers(harness, Answer(PrivacyClass.Egressable, withMemory: false));

        await RunAsync(harness.Build());

        await harness.Turns.DidNotReceive().RunTracedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_Operational_Content_As_Operational_Provenance()
    {
        var harness = Listening(From("status"));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.Operational),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_Steve_His_Own_Memory_Derived_Answer()
    {
        // ADR-0025. Under ADR-0024 this refused, which meant "hi there" was answered with
        // a citation of a decision record. The recipient is the subject; it goes.
        var harness = Listening(From("where was I?"));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("the answer text", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Explain_Rather_Than_Go_Silent_When_A_Channel_Refuses()
    {
        // A future channel whose reader is not Steve still refuses. He must be told why
        // rather than watch the message disappear.
        var harness = Listening(From("where was I?"));
        harness.Channel.SendAsync(
                Arg.Is<OutboundContent>(c => c.Provenance == ContentProvenance.ProfileDerived),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new EgressRefusedException("not addressed to the subject")));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.Operational),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Survive_A_Failing_Turn()
    {
        // The tool loop learned this the expensive way: one failure killed every turn
        // after it. A gateway that dies on a bad question is worse than useless.
        var harness = Listening(From("boom"));
        harness.Turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns<Task<TurnResult>>(_ => throw new InvalidOperationException("model died"));

        await RunAsync(harness.Build());
    }

    [Fact]
    public async Task Should_Let_The_Frontier_Answer_Rather_Than_The_Local_Model()
    {
        // ADR-0026, the whole point: the local model feeds the answer, it does not write it.
        var harness = Listening(From("what should I do about the valve"));
        harness.Options = Configured(frontier: true);
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "the frontier's answer", 6, 800));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text == "the frontier's answer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Run_The_Local_Model_When_The_Frontier_Answered()
    {
        var harness = Listening(From("what should I do"));
        harness.Options = Configured(frontier: true);
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "the frontier's answer", 6, 800));

        await RunAsync(harness.Build());

        await harness.Turns.DidNotReceive().RunTracedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Fall_Back_To_The_Local_Model_When_The_Frontier_Fails()
    {
        // A subscription hiccup should cost answer quality, never the gateway.
        var harness = Listening(From("what should I do"));
        harness.Options = Configured(frontier: true);
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<AugmentedTurnResult>>(_ => throw new InvalidOperationException("codex down"));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("the answer text", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_When_A_Fallback_Answer_Came_From_The_Local_Model()
    {
        // A worse answer must never be passed off silently as the good one.
        var harness = Listening(From("what should I do"));
        harness.Options = Configured(frontier: true);
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<AugmentedTurnResult>>(_ => throw new InvalidOperationException("codex down"));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text.Contains("answered locally", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Caption_An_Image_Locally_And_Send_It_As_Context()
    {
        var harness = Listening(new InboundMessage("owner", "chan-1", "what is this", DateTimeOffset.UnixEpoch)
        {
            Attachments = [new InboundAttachment("bolt.png", "https://cdn/bolt.png", "image/png", 2048)],
        });
        harness.Options = Configured(frontier: true);
        harness.Rest.DownloadAsync("https://cdn/bolt.png", Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>([1, 2, 3]));
        harness.Vision.DescribeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("a rusted hex bolt");
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "a 1/2 inch bolt", 2, 300));

        await RunAsync(harness.Build());

        // The caption must arrive as GATED context, never inside the question — the
        // question is appended to the frontier prompt ungated, and an image is LocalOnly
        // under D-012. An earlier version put it in the question and leaked it.
        await harness.Augmented.Received(1).RunAsync(
            Arg.Is<string>(question => !question.Contains("a rusted hex bolt", StringComparison.Ordinal)),
            Arg.Is<IReadOnlyList<string>>(context =>
                context.Any(line => line.Contains("a rusted hex bolt", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Journal_The_Exchange_So_The_Next_Message_Has_A_Memory()
    {
        var harness = Listening(From("remember this"));
        Answers(harness, Answer(PrivacyClass.LocalOnly, withMemory: false));

        await RunAsync(harness.Build());

        await harness.TurnStore.Received(1).CompleteTurnAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Carry_Prior_Exchanges_Into_The_Frontier_Turn()
    {
        // ConversationWindow.Empty was the old behaviour: every message was turn one.
        var harness = Listening(From("and what about tuesday"));
        harness.Options = Configured(frontier: true);
        harness.TurnStore
            .RecentCompletedTurnsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OneCompletedTurnAsync("what did I lift monday", "225 for five"));
        harness.Augmented
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnResult(Guid.NewGuid(), "you rested", 1, 100));

        await RunAsync(harness.Build());

        await harness.Augmented.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(prior =>
                prior.Any(line => line.Contains("225 for five", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ConversationTurn> OneCompletedTurnAsync(
        string message, string response)
    {
        yield return new ConversationTurn(
            1,
            new ConversationTurnRequest(Guid.NewGuid(), Guid.NewGuid(), message, DateTimeOffset.UnixEpoch),
            Guid.NewGuid(),
            ConversationTurnState.Completed,
            response,
            DateTimeOffset.UnixEpoch);
        await Task.CompletedTask;
    }
}
