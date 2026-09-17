using Dami.Contracts.Gateways;
using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Dami.Core.Gallery;
using Dami.Core.Turns;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordGatewayWorkerTests
{
    private static DiscordOptions Configured() => new()
    {
        Token = "a-token",
        OwnerUserId = "347544641295613953",
        Enabled = true,
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

        public IProgressiveEgressChannel Progressive { get; init; } =
            Substitute.For<IProgressiveEgressChannel>();

        public IAugmentedTurn Augmented { get; init; } = Substitute.For<IAugmentedTurn>();

        public IVisionClient Vision { get; init; } = Substitute.For<IVisionClient>();

        public IImageGenerator Images { get; init; } = Substitute.For<IImageGenerator>();

        public IPortraitGenerator Portraits { get; init; } = Substitute.For<IPortraitGenerator>();

        public IFrontierRecall Recall { get; init; } = RecallStub();

        public IFrontierRemember Remember { get; init; } = RememberStub();

        public IFrontierScheduling Scheduling { get; init; } = SchedulingStub();

        public IDiscordRest Rest { get; init; } = Substitute.For<IDiscordRest>();

        public IConversationSessionStore Sessions { get; init; } =
            Substitute.For<IConversationSessionStore>();

        /// <summary>Empty history by default; a test that wants some overrides it.</summary>
        public IConversationTurnStore TurnStore { get; init; } = EmptyHistory();

        public ISurfacingQueue Surfacings { get; init; } = NothingNoticed();

        public DiscordOptions Options { get; set; } = Configured();

        private static IFrontierRecall RecallStub()
        {
            var recall = Substitute.For<IFrontierRecall>();
            recall.Tool.Returns(new FrontierTool(
                "recall", "look it up", System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement));
            return recall;
        }

        private static IFrontierRemember RememberStub()
        {
            var remember = Substitute.For<IFrontierRemember>();
            remember.Tool.Returns(new FrontierTool(
                "remember", "keep it", System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement));
            return remember;
        }

        private static IFrontierScheduling SchedulingStub()
        {
            var scheduling = Substitute.For<IFrontierScheduling>();
            var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
            scheduling.ScheduleTool.Returns(new FrontierTool("schedule", "draft", schema));
            scheduling.ConfirmTool.Returns(new FrontierTool("confirm_schedule", "confirm", schema));
            return scheduling;
        }

        private static IFrontierFitness FitnessStub()
        {
            var fitness = Substitute.For<IFrontierFitness>();
            var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
            fitness.SetsToolAsync(Arg.Any<CancellationToken>()).Returns(new FrontierTool("log_sets", "l", schema));
            fitness.CardioTool.Returns(new FrontierTool("log_cardio", "l", schema));
            return fitness;
        }

        private static IFrontierResearch ResearchStub()
        {
            var research = Substitute.For<IFrontierResearch>();
            var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
            research.SearchTool.Returns(new FrontierTool("search_web", "s", schema));
            research.ReadTool.Returns(new FrontierTool("read_page", "r", schema));
            return research;
        }

        private static IStandingLessons LessonsStub()
        {
            var lessons = Substitute.For<IStandingLessons>();
            lessons.LinesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
            return lessons;
        }

        private static IFrontierToday TodayStub()
        {
            var today = Substitute.For<IFrontierToday>();
            today.Tool.Returns(new FrontierTool("today", "t", System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement));
            return today;
        }

        private static IFrontierCode CodeStub()
        {
            var code = Substitute.For<IFrontierCode>();
            code.Tools.Returns(Array.Empty<FrontierTool>());
            return code;
        }

        private static ISurfacingQueue NothingNoticed()
        {
            var queue = Substitute.For<ISurfacingQueue>();
            queue.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(NoSurfacingsAsync());
            return queue;
        }

        private static async IAsyncEnumerable<Surfacing> NoSurfacingsAsync()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static IConversationTurnStore EmptyHistory()
        {
            var store = Substitute.For<IConversationTurnStore>();
            store.RecentCompletedTurnsAsync(
                    Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(NoTurnsAsync());
            return store;
        }

        private DiscordAnswerer Answerer()
        {
            var bundle = new FrontierToolBundle(
                this.Images, this.Portraits, this.Recall, this.Remember, Substitute.For<IFrontierLesson>(), this.Scheduling,
                Substitute.For<IGallerySearch>(), Substitute.For<IGalleryPictures>(), FitnessStub(), ResearchStub(), TodayStub(),
                CodeStub(), NullLogger<FrontierToolBundle>.Instance);
            var vision = new DiscordVision(
                this.Vision, this.Rest, this.Options, NullLogger<DiscordVision>.Instance);
            return new DiscordAnswerer(
                this.Channel,
                this.Augmented,
                new DiscordReplyStreamer(this.Progressive),
                bundle,
                vision,
                this.Sessions,
                this.TurnStore,
                this.Surfacings,
                LessonsStub(),
                TimeProvider.System,
                this.Options,
                NullLogger<DiscordAnswerer>.Instance);
        }

        public DiscordGatewayWorker Build()
        {
            var answerer = this.Answerer();
            return new DiscordGatewayWorker(
                this.Authority,
                this.Channel,
                answerer,
                new DiscordImageResponder(
                    this.Images, this.Portraits, this.Channel, NullLogger<DiscordImageResponder>.Instance),
                new DiscordTypingIndicator(
                    this.Rest, this.Options, NullLogger<DiscordTypingIndicator>.Instance),
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

    /// <summary>The frontier answers with these fragments and Discord accepts the reply.</summary>
    private static void FrontierAnswers(Harness harness, params string[] fragments)
    {
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 6, 800, FragmentsAsync(fragments)));
    }

    private static void FrontierFails(Harness harness, Exception exception)
    {
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns<Task<AugmentedTurnStream>>(_ => throw exception);
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
        // What matters is that "status" is answered WITHOUT assembling context, since
        // doing so would retrieve Steve's memories in order to report service health.
        var harness = Listening(From("status"));
        FrontierAnswers(harness, "should not be asked");

        await RunAsync(harness.Build());

        await harness.Augmented.DidNotReceive().StreamAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
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
        FrontierAnswers(harness, "the answer text");

        await RunAsync(harness.Build());

        await harness.Progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content =>
                content.Text == "the answer text"
                && content.Provenance == ContentProvenance.ProfileDerived),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Explain_Rather_Than_Go_Silent_When_A_Channel_Refuses()
    {
        // A future channel whose reader is not Steve still refuses. He must be told why
        // rather than watch the message disappear.
        var harness = Listening(From("where was I?"));
        FrontierAnswers(harness, "the answer text");
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new EgressRefusedException("not addressed to the subject"));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("ADR-0025", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Survive_A_Failing_Turn()
    {
        // The tool loop learned this the expensive way: one failure killed every turn
        // after it. A gateway that dies on a bad question is worse than useless.
        var harness = Listening(From("boom"));
        FrontierFails(harness, new InvalidOperationException("model died"));

        await RunAsync(harness.Build());
    }

    [Fact]
    public async Task Should_Survive_A_Canceled_Turn_When_The_Host_Is_Not_Stopping()
    {
        var harness = Listening(From("time out"));
        FrontierFails(harness, new OperationCanceledException("turn deadline elapsed"));

        await RunAsync(harness.Build());
    }

    [Fact]
    public async Task Should_Let_The_Frontier_Answer_Rather_Than_The_Local_Model()
    {
        // ADR-0026, the whole point: the local model feeds the answer, it does not write it.
        var harness = Listening(From("what should I do about the valve"));
        FrontierAnswers(harness, "the frontier's answer");

        await RunAsync(harness.Build());

        await harness.Progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content => content.Text == "the frontier's answer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Show_Typing_Before_It_Thinks()
    {
        var harness = Listening(From("take your time"));
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, FragmentsAsync("done")));

        await RunAsync(harness.Build());

        Received.InOrder(() =>
        {
            _ = harness.Rest.PostTypingAsync("chan-1", Arg.Any<CancellationToken>());
            _ = harness.Augmented.StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_Refresh_Typing_While_The_Turn_Is_Still_Running()
    {
        var harness = Listening(From("take longer"));
        harness.Options.TypingRefresh = TimeSpan.FromMilliseconds(5);
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, DelayedFragmentAsync()));

        await RunAsync(harness.Build());

        Assert.True(harness.Rest.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IDiscordRest.PostTypingAsync)) >= 2);
    }

    [Fact]
    public async Task Should_Create_Then_Update_A_Streamed_Frontier_Reply()
    {
        var harness = Listening(From("write slowly"));
        harness.Progressive.BeginAsync(
                Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(
                Guid.NewGuid(), 0, 0,
                FragmentsAsync("first ", new string('x', 100), "second")));

        await RunAsync(harness.Build());

        await harness.Progressive.Received(1).BeginAsync(
            Arg.Is<OutboundContent>(content => content.Text == "first "),
            Arg.Any<CancellationToken>());
        await harness.Progressive.Received(2).UpdateAsync(
            "message-1",
            Arg.Any<OutboundContent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Should_Have_No_Local_Model_To_Answer_With()
    {
        // ADR-0028. The fallback was removed by taking away the seam it ran through, so
        // that it cannot come back as "just one catch block" — this pins the constructor.
        var dependencies = typeof(DiscordGatewayWorker).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(ITracedTurnRunner), dependencies);
    }

    [Fact]
    public async Task Should_Say_The_Frontier_Failed_Rather_Than_Answer_Locally()
    {
        // 2026-09-03 23:04: a Discord 429 mid-stream became a qwen3 answer. Never again.
        var harness = Listening(From("what should I do"));
        FrontierFails(harness, new InvalidOperationException("codex down"));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("did not answer", StringComparison.Ordinal)
                && content.Text.Contains("codex down", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await harness.Progressive.DidNotReceive().BeginAsync(
            Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_The_Frontier_Timed_Out_Rather_Than_Answer_Locally()
    {
        // 2026-09-03 23:19: a ten-minute Codex hang became a qwen3 answer. Never again.
        var harness = Listening(From("what should I do"));
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 0, 0, CanceledFragmentsAsync()));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Provenance == ContentProvenance.Operational
                && content.Text.Contains("deadline", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Journal_A_Turn_The_Frontier_Did_Not_Answer()
    {
        // An explanation is not an exchange; journaling it would feed the next turn's
        // window with "the frontier did not answer" as if Dami had said it.
        var harness = Listening(From("what should I do"));
        FrontierFails(harness, new InvalidOperationException("codex down"));

        await RunAsync(harness.Build());

        await harness.TurnStore.DidNotReceive().CompleteTurnAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Caption_An_Image_Locally_And_Send_It_As_Context()
    {
        var harness = Listening(new InboundMessage("owner", "chan-1", "what is this", DateTimeOffset.UnixEpoch)
        {
            Attachments = [new InboundAttachment("bolt.png", "https://cdn/bolt.png", "image/png", 2048)],
        });
        harness.Rest.DownloadAsync("https://cdn/bolt.png", Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>([1, 2, 3]));
        harness.Vision.DescribeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("a rusted hex bolt");
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(
                Guid.NewGuid(), 2, 300, FragmentsAsync("a 1/2 inch bolt")));

        await RunAsync(harness.Build());

        // The caption must arrive as GATED context, never inside the question — the
        // question is appended to the frontier prompt ungated, and an image is LocalOnly
        // under D-012. An earlier version put it in the question and leaked it.
        await harness.Augmented.Received(1).StreamAsync(
            Arg.Is<string>(question => !question.Contains("a rusted hex bolt", StringComparison.Ordinal)),
            Arg.Is<IReadOnlyList<string>>(context =>
                context.Any(line => line.Contains("a rusted hex bolt", StringComparison.Ordinal))),
            Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Degrade_Visibly_When_Vision_Exceeds_Its_Budget()
    {
        var harness = Listening(new InboundMessage("owner", "chan-1", "what is this", DateTimeOffset.UnixEpoch)
        {
            Attachments = [new InboundAttachment("slow.png", "https://cdn/slow.png", "image/png", 2048)],
        });
        harness.Options.VisionTimeout = TimeSpan.FromMilliseconds(10);
        harness.Rest.DownloadAsync("https://cdn/slow.png", Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>([1, 2, 3]));
        harness.Vision.DescribeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
                await Task.Delay(Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(2)));
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 1, 0, FragmentsAsync("I could not see it")));

        await RunAsync(harness.Build());

        await harness.Augmented.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(context =>
                context.Any(line => line.Contains("could not be read", StringComparison.Ordinal))),
            Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Generate_And_Attach_An_Explicitly_Requested_Image()
    {
        var harness = Listening(From("create an image of a red barn at sunset"));
        harness.Images.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage(
                "barn.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png",
                "a red barn at sunset"));

        await RunAsync(harness.Build());

        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Attachments.Count == 1
                && content.Attachments[0].FileName == "barn.png"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_A_Portrait_When_Steve_Asks_To_See_Her()
    {
        // 2026-09-04 20:20, Discord: "Let's see an image of you painting your toes" went
        // to the frontier, which described a pedicure and attached nothing.
        var harness = Listening(From("Let's see an image of you painting your toes"));
        harness.Portraits.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage(
                "dami-1.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png", "prompt"));

        await RunAsync(harness.Build());

        await harness.Portraits.Received(1).GenerateAsync(
            "Dami painting her toes", Arg.Any<CancellationToken>());
        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Attachments.Count == 1),
            Arg.Any<CancellationToken>());
        await harness.Augmented.DidNotReceive().StreamAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Offer_The_Frontier_The_Picture_Tools()
    {
        // ADR-0030: the frontier decides; the four-prefix grammar was the whole reason
        // "let's see an image of you" produced prose.
        var harness = Listening(From("what are you up to right now?"));
        FrontierAnswers(harness, "just reading");

        await RunAsync(harness.Build());

        await harness.Augmented.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<FrontierToolbox>(tools =>
                tools.Tools.Any(tool => tool.Name == FrontierToolBundle.MAKE_PORTRAIT)
                && tools.Tools.Any(tool => tool.Name == FrontierToolBundle.MAKE_IMAGE)
                && tools.Tools.Any(tool => tool.Name == "recall")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Attach_A_Picture_The_Frontier_Made_With_Its_Tool()
    {
        var harness = Listening(From("show me what you're doing"));
        harness.Portraits.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("dami-2.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png", "p"));
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new AugmentedTurnStream(
                Guid.NewGuid(), 0, 0, CallingTheToolAsync(call.ArgAt<FrontierToolbox>(2))));

        await RunAsync(harness.Build());

        await harness.Portraits.Received(1).GenerateAsync("reading on the sofa", Arg.Any<CancellationToken>());
        await harness.Channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content =>
                content.Attachments.Count == 1 && content.Attachments[0].FileName == "dami-2.png"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A frontier that calls make_portrait mid-stream, as the app-server would.</summary>
    private static async IAsyncEnumerable<string> CallingTheToolAsync(FrontierToolbox tools)
    {
        using var arguments = System.Text.Json.JsonDocument.Parse("""{"scene":"reading on the sofa"}""");
        var result = await tools.Handler.HandleAsync(
            new FrontierToolCall("call-1", FrontierToolBundle.MAKE_PORTRAIT, arguments.RootElement),
            CancellationToken.None);
        yield return result.Success ? "here you go" : result.Text;
    }

    [Fact]
    public async Task Should_Tell_The_Frontier_What_She_Noticed_And_Mark_It_Delivered_Once_Answered()
    {
        // The charter's sentence, delivered: a surfacing rides the next message rather than
        // being pushed (ADR-0014 unsigned), and counts as delivered once she answered.
        var harness = Listening(From("morning"));
        var surfacing = new Surfacing(Guid.NewGuid(), "fitness-review", "Your week in the gym", "3 sessions, 40 sets", 0.7, DateTimeOffset.UnixEpoch);
        harness.Surfacings.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OneSurfacingAsync(surfacing));
        FrontierAnswers(harness, "morning! good week in the gym");

        await RunAsync(harness.Build());

        await harness.Augmented.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(lines => lines.Any(line => line.Contains("Dami noticed", StringComparison.Ordinal) && line.Contains("40 sets", StringComparison.Ordinal))),
            Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
        await harness.Surfacings.Received(1).DeliverAsync(surfacing.SurfacingId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Mark_Noticed_Things_Delivered_When_The_Frontier_Failed()
    {
        var harness = Listening(From("morning"));
        var surfacing = new Surfacing(Guid.NewGuid(), "fitness-review", "t", "b", 0.7, DateTimeOffset.UnixEpoch);
        harness.Surfacings.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OneSurfacingAsync(surfacing));
        FrontierFails(harness, new InvalidOperationException("codex down"));

        await RunAsync(harness.Build());

        await harness.Surfacings.DidNotReceive().DeliverAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<Surfacing> OneSurfacingAsync(Surfacing surfacing)
    {
        yield return surfacing;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Should_Journal_The_Exchange_So_The_Next_Message_Has_A_Memory()
    {
        var harness = Listening(From("remember this"));
        FrontierAnswers(harness, "noted");

        await RunAsync(harness.Build());

        await harness.TurnStore.Received(1).CompleteTurnAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), "noted", Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Carry_Prior_Exchanges_Into_The_Frontier_Turn()
    {
        // ConversationWindow.Empty was the old behaviour: every message was turn one.
        var harness = Listening(From("and what about tuesday"));
        harness.TurnStore
            .RecentCompletedTurnsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OneCompletedTurnAsync("what did I lift monday", "225 for five"));
        harness.Progressive.BeginAsync(Arg.Any<OutboundContent>(), Arg.Any<CancellationToken>())
            .Returns("message-1");
        harness.Augmented
            .StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(new AugmentedTurnStream(Guid.NewGuid(), 1, 100, FragmentsAsync("you rested")));

        await RunAsync(harness.Build());

        await harness.Augmented.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(prior =>
                prior.Any(line => line.Contains("225 for five", StringComparison.Ordinal))),
            Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
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

    private static async IAsyncEnumerable<string> FragmentsAsync(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            yield return fragment;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<string> DelayedFragmentAsync()
    {
        await Task.Delay(25);
        yield return "done";
    }

    private static async IAsyncEnumerable<string> CanceledFragmentsAsync()
    {
        await Task.Yield();
        throw new OperationCanceledException("frontier deadline elapsed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
