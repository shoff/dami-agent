using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Core.Turns;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Turns;

public sealed class ToolLoopRunnerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 23, 0, 0, TimeSpan.Zero);

    private readonly List<ExecutionEvent> events = [];
    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    public ToolLoopRunnerTests()
    {
        this.eventStore.AppendAsync(
            Arg.Do<ExecutionEvent>(this.events.Add), Arg.Any<CancellationToken>())
            .Returns(1L);
    }

    [Fact]
    public async Task RunAsync_Should_Execute_One_Tool_And_Return_The_Follow_Up_Answer()
    {
        var invocation = CreateInvocation();
        var schema = CreateSchema(invocation.CapabilityId);
        var result = new CapabilityExecutionResult(
            "file contents", new Dictionary<string, string> { ["path"] = "notes.txt" });
        var model = new RecordingToolCallingClient(
            [ToolModelTurn.ForCall("call-1", invocation), ToolModelTurn.ForAnswer("final answer")]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });
        var traceId = Guid.NewGuid();
        var parentSpanId = Guid.NewGuid();

        var answer = await runner.RunAsync(
            traceId, parentSpanId, "read my notes", [schema],
            PrivacyClass.Egressable, ExecutionOrigin.SelfAudit, CancellationToken.None);

        Assert.Equal("final answer", answer);
        this.AssertSuccessfulEvents(traceId, parentSpanId);
        Assert.DoesNotContain(this.events, item => item.Label.Contains("file contents", StringComparison.Ordinal));
        Assert.Single(model.ExchangesOnCalls[1]);
        Assert.Same(result, model.ExchangesOnCalls[1][0].Result);
        Assert.Same(schema, Assert.Single(model.SchemasOnCalls[0]));
        await this.AssertExecutionProvenanceAsync(
            executor, traceId, invocation, PrivacyClass.Egressable, ExecutionOrigin.SelfAudit);
    }

    [Fact]
    public async Task RunAsync_Should_Reject_Unknown_Privacy_Before_Calling_The_Model()
    {
        var model = new RecordingToolCallingClient([ToolModelTurn.ForAnswer("unused")]);
        var runner = new ToolLoopRunner(
            model,
            Substitute.For<ICapabilityExecutor>(),
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 1 });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "question", [],
            (PrivacyClass)99, ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Empty(model.ExchangesOnCalls);
    }

    private void AssertSuccessfulEvents(Guid traceId, Guid parentSpanId)
    {
        Assert.Equal(
            [ExecutionEventType.ToolRequested, ExecutionEventType.ToolStarted, ExecutionEventType.ToolCompleted],
            this.events.Select(item => item.Type));
        Assert.All(this.events, item => Assert.Equal(traceId, item.TraceId));
        Assert.All(this.events, item => Assert.Equal(parentSpanId, item.ParentSpanId));
        Assert.Single(this.events.Select(item => item.SpanId).Distinct());
        Assert.All(this.events, item => Assert.Equal("call-1", item.Metadata!["call_id"]));
    }

    private async Task AssertExecutionProvenanceAsync(
        ICapabilityExecutor executor,
        Guid traceId,
        CapabilityInvocation invocation,
        PrivacyClass privacy,
        ExecutionOrigin origin)
    {
        await executor.Received(1).ExecuteAsync(
            Arg.Is<CapabilityExecutionRequest>(request =>
                request.TraceId == traceId
                && request.SpanId == this.events[0].SpanId
                && request.Privacy == privacy
                && request.Origin == origin
                && ReferenceEquals(request.Invocation, invocation)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Hand_A_Failed_Tool_Back_To_The_Model_And_Keep_Going()
    {
        // Regression, and the point of the change. A tool that throws used to end the
        // whole turn: a local 8B asking to read the literal path "path" took down an
        // entire advisory run before the model could try anything else. The failure is
        // now a turn in the conversation — recorded as ToolFailed exactly as before, then
        // returned so the model can correct itself inside the bound it already has.
        var invocation = CreateInvocation();
        var model = new RecordingToolCallingClient(
            [ToolModelTurn.ForCall("call-1", invocation), ToolModelTurn.ForAnswer("recovered")]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<CapabilityExecutionResult>>(_ => throw new IOException("read failed"));
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });

        var answer = await runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal("recovered", answer);
        Assert.Equal(
            [ExecutionEventType.ToolRequested, ExecutionEventType.ToolStarted, ExecutionEventType.ToolFailed],
            this.events.Select(item => item.Type));

        // The model has to be told what went wrong, or it cannot do better next call.
        var exchange = Assert.Single(model.ExchangesOnCalls[1]);
        Assert.False(exchange.Succeeded);
        Assert.Null(exchange.Result);
        Assert.Contains("read failed", exchange.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Still_Stop_When_Every_Call_In_The_Bound_Fails()
    {
        // Recovering from failures must not become an unbounded retry loop; the existing
        // call bound is what stops it.
        var invocation = CreateInvocation();
        var model = new RecordingToolCallingClient(
            [ToolModelTurn.ForCall("call-1", invocation), ToolModelTurn.ForCall("call-2", invocation),
             ToolModelTurn.ForCall("call-3", invocation)]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<CapabilityExecutionResult>>(_ => throw new IOException("read failed"));
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Contains("bound of 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_Record_Cancelled_Tool_After_The_Caller_Token_Is_Cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var invocation = CreateInvocation();
        var model = new RecordingToolCallingClient([ToolModelTurn.ForCall("call-1", invocation)]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), cancellation.Token)
            .Returns(_ => CancelExecutionAsync(cancellation));
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, cancellation.Token));

        var terminalEvent = Assert.Single(
            this.events, item => item.Type == ExecutionEventType.ToolFailed);
        Assert.Equal(ExecutionStatus.Cancelled, terminalEvent.Status);
        await this.eventStore.Received().AppendAsync(terminalEvent, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_Should_Give_Each_Model_Call_A_Point_In_Time_Exchange_Snapshot()
    {
        var invocation = CreateInvocation();
        var result = new CapabilityExecutionResult(
            "file contents", new Dictionary<string, string> { ["path"] = "notes.txt" });
        var model = new RetainingToolCallingClient(invocation);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });

        await runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Empty(model.FirstCallExchanges);
    }

    [Fact]
    public async Task RunAsync_Should_Stop_Before_Executing_More_Than_The_Configured_Bound()
    {
        var invocation = CreateInvocation();
        var result = new CapabilityExecutionResult(
            "file contents", new Dictionary<string, string> { ["path"] = "notes.txt" });
        var model = new RecordingToolCallingClient(
            [ToolModelTurn.ForCall("call-1", invocation), ToolModelTurn.ForCall("call-2", invocation)]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 1 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Contains("bound of 1", exception.Message, StringComparison.Ordinal);
        await executor.Received(1).ExecuteAsync(
            Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal(3, this.events.Count);
    }

    [Fact]
    public async Task RunAsync_Should_Not_Misreport_Event_Persistence_Failure_As_Tool_Failure()
    {
        var invocation = CreateInvocation();
        var result = new CapabilityExecutionResult(
            "file contents", new Dictionary<string, string> { ["path"] = "notes.txt" });
        var model = new RecordingToolCallingClient([ToolModelTurn.ForCall("call-1", invocation)]);
        var executor = Substitute.For<ICapabilityExecutor>();
        executor.ExecuteAsync(Arg.Any<CapabilityExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);
        this.eventStore.AppendAsync(
                Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.ToolCompleted),
                Arg.Any<CancellationToken>())
            .Returns<Task<long>>(_ => throw new IOException("event store unavailable"));
        var runner = new ToolLoopRunner(
            model,
            executor,
            this.eventStore,
            new FakeTimeProvider(now),
            new ToolLoopOptions { MaxToolCalls = 2 });

        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(
            Guid.NewGuid(), Guid.NewGuid(), "read my notes",
            [CreateSchema(invocation.CapabilityId)],
            PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.DoesNotContain(this.events, item => item.Type == ExecutionEventType.ToolFailed);
    }

    private static CapabilityInvocation CreateInvocation()
    {
        var arguments = JsonSerializer.SerializeToElement(new { path = "notes.txt" });
        return new CapabilityInvocation(Guid.NewGuid(), arguments);
    }

    private static CapabilityToolSchema CreateSchema(Guid capabilityId)
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { path = new { type = "string" } },
            required = new[] { "path" },
        });
        return new CapabilityToolSchema(
            capabilityId, "read_file", "Read one workspace file.", parameters);
    }

    private static async Task<CapabilityExecutionResult> CancelExecutionAsync(
        CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
        return await Task.FromCanceled<CapabilityExecutionResult>(cancellation.Token);
    }

    private sealed class RecordingToolCallingClient(
        IEnumerable<ToolModelTurn> turns) : IToolCallingChatClient
    {
        private readonly Queue<ToolModelTurn> turns = new(turns);

        public List<IReadOnlyList<ToolExecutionExchange>> ExchangesOnCalls { get; } = [];

        public List<IReadOnlyList<CapabilityToolSchema>> SchemasOnCalls { get; } = [];

        public Task<ToolModelTurn> NextAsync(
            string prompt,
            IReadOnlyList<CapabilityToolSchema> toolSchemas,
            IReadOnlyList<ToolExecutionExchange> exchanges,
            CancellationToken cancellationToken)
        {
            this.SchemasOnCalls.Add(toolSchemas.ToArray());
            this.ExchangesOnCalls.Add(exchanges.ToArray());
            return Task.FromResult(this.turns.Dequeue());
        }
    }

    private sealed class RetainingToolCallingClient(
        CapabilityInvocation invocation) : IToolCallingChatClient
    {
        private int callCount;

        public IReadOnlyList<ToolExecutionExchange> FirstCallExchanges { get; private set; } = [];

        public Task<ToolModelTurn> NextAsync(
            string prompt,
            IReadOnlyList<CapabilityToolSchema> toolSchemas,
            IReadOnlyList<ToolExecutionExchange> exchanges,
            CancellationToken cancellationToken)
        {
            if (this.callCount++ == 0)
            {
                this.FirstCallExchanges = exchanges;
                return Task.FromResult(ToolModelTurn.ForCall("call-1", invocation));
            }

            return Task.FromResult(ToolModelTurn.ForAnswer("done"));
        }
    }
}
