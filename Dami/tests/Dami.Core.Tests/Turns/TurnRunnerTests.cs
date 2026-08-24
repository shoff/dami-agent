using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Turns;

/// <summary>The interactive turn: traced, routed, grounded.</summary>
public sealed class TurnRunnerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 14, 0, 0, TimeSpan.Zero);

    private readonly IContextBuilder contextBuilder = Substitute.For<IContextBuilder>();
    private readonly IModelRouter modelRouter = Substitute.For<IModelRouter>();
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();
    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();
    private readonly IObservationCorpus observationCorpus = Substitute.For<IObservationCorpus>();
    private readonly IIdentityProvider identityProvider = Substitute.For<IIdentityProvider>();
    private readonly ICapabilityToolResolver toolResolver = Substitute.For<ICapabilityToolResolver>();
    private readonly IToolLoopRunner toolLoop = Substitute.For<IToolLoopRunner>();

    [Fact]
    public async Task RunAsync_Should_Emit_A_UserTurn_Trace_From_Start_To_Completion()
    {
        this.Arrange();

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        await this.eventStore.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.TraceCompleted
                && item.Origin == ExecutionOrigin.UserTurn),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Resolve_Selected_Tools_And_Use_The_Tool_Loop()
    {
        this.Arrange();
        var schema = CreateToolSchema();
        var toolResolver = Substitute.For<ICapabilityToolResolver>();
        toolResolver.ResolveAsync("read notes", Arg.Any<CancellationToken>())
            .Returns([schema]);
        var toolLoop = Substitute.For<IToolLoopRunner>();
        toolLoop.RunAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CapabilityToolSchema>>(), Arg.Any<CancellationToken>())
            .Returns("tool-backed answer");
        this.identityProvider.Preamble.Returns("You are Dami, Steve's assistant.");
        var runner = new TurnRunner(
            this.contextBuilder, this.modelRouter, this.chatClient, this.eventStore,
            this.observationCorpus, this.identityProvider, toolResolver, toolLoop,
            new FakeTimeProvider(now), NullLogger<TurnRunner>.Instance);

        var result = await runner.RunAsync("read notes", CancellationToken.None);

        Assert.Equal("tool-backed answer", result.Answer);
        await toolResolver.Received(1).ResolveAsync("read notes", Arg.Any<CancellationToken>());
        await toolLoop.Received(1).RunAsync(
            result.TraceId,
            Arg.Is<Guid>(spanId => spanId != Guid.Empty),
            Arg.Is<string>(prompt => prompt.Contains("read notes", StringComparison.Ordinal)),
            Arg.Is<IReadOnlyList<CapabilityToolSchema>>(items =>
                items.Count == 1 && ReferenceEquals(items[0], schema)),
            Arg.Any<CancellationToken>());
        await this.chatClient.DidNotReceive().CompleteAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Parent_Tool_Events_To_The_Capability_Selection()
    {
        this.Arrange();
        ExecutionEvent? selection = null;
        this.eventStore.AppendAsync(
                Arg.Do<ExecutionEvent>(item =>
                    selection = item.Type == ExecutionEventType.CapabilitySelected ? item : selection),
                Arg.Any<CancellationToken>())
            .Returns(1L);

        var result = await this.CreateRunner().RunAsync("read notes", CancellationToken.None);

        Assert.NotNull(selection);
        await this.toolLoop.Received(1).RunAsync(
            result.TraceId, selection.SpanId, Arg.Any<string>(),
            Arg.Any<IReadOnlyList<CapabilityToolSchema>>(), Arg.Any<CancellationToken>());
    }

    private static CapabilityToolSchema CreateToolSchema()
    {
        return new CapabilityToolSchema(
            Guid.NewGuid(), "read_file", "Read a file.",
            JsonSerializer.SerializeToElement(new { type = "object" }));
    }

    private Task<string> CaptureToolPromptAsync(Action<string> capture)
    {
        return this.toolLoop.RunAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<string>(capture),
            Arg.Any<IReadOnlyList<CapabilityToolSchema>>(), Arg.Any<CancellationToken>());
    }

    private Task<string> AnyToolLoopCallAsync()
    {
        return this.toolLoop.RunAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<CapabilityToolSchema>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Classify_Context_Bearing_Turns_LocalOnly()
    {
        this.Arrange();

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        this.modelRouter.Received(1).Route("synthesis", PrivacyClass.LocalOnly);
    }

    [Fact]
    public async Task RunAsync_Should_Put_Beliefs_And_Memories_In_The_Prompt()
    {
        this.Arrange(
            beliefs: ["prefers evidence to assertion"],
            memories: ["worked on the transport codec"]);
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text)
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Contains("prefers evidence to assertion", prompt);
    }

    [Fact]
    public async Task RunTracedAsync_Should_Use_The_Reserved_Trace_And_Recent_Conversation()
    {
        this.Arrange();
        var traceId = Guid.NewGuid();
        var request = new ConversationTurnRequest(
            Guid.NewGuid(), Guid.NewGuid(), "earlier question", now.AddMinutes(-2));
        var earlier = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Completed,
            "earlier answer", now.AddMinutes(-1));
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text).Returns("current answer");

        var result = await ((ITracedTurnRunner)this.CreateRunner()).RunTracedAsync(
            traceId, "current question", new ConversationWindow([earlier], 20), CancellationToken.None);

        Assert.Equal(traceId, result.TraceId);
        Assert.Contains("Steve: earlier question", prompt);
        Assert.Contains("Dami: earlier answer", prompt);
        Assert.Contains("Steve: current question", prompt);
    }

    [Fact]
    public async Task RunAsync_Should_Lead_The_Prompt_With_The_Identity_Preamble()
    {
        this.Arrange();
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text)
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.StartsWith("You are Dami", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Say_Undated_For_An_EpochZero_Memory()
    {
        var epochZero = new RetrievedItem(
            "observation", Guid.NewGuid(), "a migrated memory", DateTimeOffset.UnixEpoch);
        this.contextBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AssembledContext([epochZero], [], 120));
        this.modelRouter.Route(Arg.Any<string>(), Arg.Any<PrivacyClass>())
            .Returns(new ModelRoute(ModelTier.Local, PrivacyClass.LocalOnly, "test"));
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text)
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Contains("[memory undated]", prompt);
    }

    [Fact]
    public async Task RunAsync_Should_Anchor_The_Prompt_To_Today()
    {
        this.Arrange();
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text)
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Contains("Today is 2026-08-23", prompt);
    }

    [Fact]
    public async Task RunAsync_Should_Report_The_Context_Cost_In_The_Trace()
    {
        this.Arrange();

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.ContextRetrieved
                && item.Label.Contains("~120 tokens")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Record_A_Failed_Turn_And_Rethrow()
    {
        this.Arrange();
        this.AnyToolLoopCallAsync()
            .Returns<Task<string>>(_ => throw new InvalidOperationException("model down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateRunner().RunAsync("a question", CancellationToken.None));

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.TraceFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Return_The_Route_It_Took()
    {
        this.Arrange();

        var result = await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Equal(ModelTier.Local, result.Route.Tier);
    }

    [Fact]
    public async Task RunAsync_Should_Tell_The_Model_When_No_Memories_Were_Found()
    {
        this.Arrange(memories: []);
        string? prompt = null;
        this.CaptureToolPromptAsync(text => prompt = text)
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Contains("No relevant memories were found", prompt);
    }

    [Fact]
    public async Task RunAsync_Should_Record_The_Interaction_Into_The_Corpus()
    {
        this.Arrange();

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        await this.observationCorpus.Received(1).RecordAsync(
            Arg.Is<Observation>(item => item.Source == "chat" && item.Body.Contains("a question")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Not_Record_A_Failed_Turn_As_An_Interaction()
    {
        this.Arrange();
        this.AnyToolLoopCallAsync()
            .Returns<Task<string>>(_ => throw new InvalidOperationException("down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateRunner().RunAsync("a question", CancellationToken.None));

        await this.observationCorpus.DidNotReceive().RecordAsync(
            Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginStreamingAsync_Should_Yield_Fragments_And_Complete_The_Trace_When_Drained()
    {
        this.Arrange();
        this.chatClient.StreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FragmentsAsync("Hel", "lo"));

        var stream = await this.CreateRunner().BeginStreamingAsync("a question", CancellationToken.None);
        var collected = new List<string>();
        await foreach (var fragment in stream.Tokens)
        {
            collected.Add(fragment);
        }

        Assert.Equal(["Hel", "lo"], collected);
        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.TraceCompleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginStreamingAsync_Should_Not_Complete_Until_Drained()
    {
        this.Arrange();
        this.chatClient.StreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FragmentsAsync("x"));

        await this.CreateRunner().BeginStreamingAsync("a question", CancellationToken.None);

        await this.eventStore.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.TraceCompleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginStreamingAsync_Should_Record_The_Full_Interaction_After_Draining()
    {
        this.Arrange();
        this.chatClient.StreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FragmentsAsync("Hel", "lo"));

        var stream = await this.CreateRunner().BeginStreamingAsync("a question", CancellationToken.None);
        await foreach (var _ in stream.Tokens)
        {
        }

        await this.observationCorpus.Received(1).RecordAsync(
            Arg.Is<Observation>(item => item.Body.Contains("Hello")),
            Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<string> FragmentsAsync(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            yield return fragment;
        }

        await Task.CompletedTask;
    }

    private void Arrange(string[]? beliefs = null, string[]? memories = null)
    {
        var beliefItems = (beliefs ?? [])
            .Select(text => new RetrievedItem("belief", Guid.NewGuid(), text, now)).ToList();
        var memoryItems = (memories ?? [])
            .Select(text => new RetrievedItem("observation", Guid.NewGuid(), text, now)).ToList();

        this.contextBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AssembledContext(memoryItems, beliefItems, 120));
        this.modelRouter.Route(Arg.Any<string>(), Arg.Any<PrivacyClass>())
            .Returns(new ModelRoute(ModelTier.Local, PrivacyClass.LocalOnly, "test"));
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("an answer");
        this.toolResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CapabilityToolSchema>());
        this.toolLoop.RunAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CapabilityToolSchema>>(), Arg.Any<CancellationToken>())
            .Returns("an answer");
    }

    private TurnRunner CreateRunner()
    {
        this.identityProvider.Preamble.Returns("You are Dami, Steve's assistant.");
        return new TurnRunner(
            this.contextBuilder, this.modelRouter, this.chatClient, this.eventStore,
            this.observationCorpus, this.identityProvider, this.toolResolver, this.toolLoop,
            new FakeTimeProvider(now),
            NullLogger<TurnRunner>.Instance);
    }
}
