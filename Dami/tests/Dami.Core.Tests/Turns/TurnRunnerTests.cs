using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
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
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
            .Returns("an answer");

        await this.CreateRunner().RunAsync("a question", CancellationToken.None);

        Assert.Contains("prefers evidence to assertion", prompt);
    }

    [Fact]
    public async Task RunAsync_Should_Anchor_The_Prompt_To_Today()
    {
        this.Arrange();
        string? prompt = null;
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
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
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
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
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateRunner().RunAsync("a question", CancellationToken.None));

        await this.observationCorpus.DidNotReceive().RecordAsync(
            Arg.Any<Observation>(), Arg.Any<CancellationToken>());
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
    }

    private TurnRunner CreateRunner()
    {
        return new TurnRunner(
            this.contextBuilder, this.modelRouter, this.chatClient, this.eventStore,
            this.observationCorpus, new FakeTimeProvider(now), NullLogger<TurnRunner>.Instance);
    }
}
