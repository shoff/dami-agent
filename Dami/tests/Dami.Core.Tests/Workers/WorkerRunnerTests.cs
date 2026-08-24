using Dami.Contracts.Events;
using Dami.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Workers;

/// <summary>The worker discipline: child span, hard bound, failures recorded not thrown.</summary>
public sealed class WorkerRunnerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 22, 0, 0, TimeSpan.Zero);
    private static readonly Guid traceId = Guid.NewGuid();
    private static readonly Guid parentSpanId = Guid.NewGuid();

    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();
    private readonly List<ExecutionEvent> appended = [];

    public WorkerRunnerTests()
    {
        this.eventStore.AppendAsync(
            Arg.Do<ExecutionEvent>(this.appended.Add), Arg.Any<CancellationToken>())
            .Returns(1L);
    }

    [Fact]
    public async Task RunAsync_Should_Return_The_Workers_Output()
    {
        var result = await this.CreateRunner().RunAsync(
            "adder", traceId, parentSpanId, TimeSpan.FromSeconds(5),
            _ => Task.FromResult("42"), CancellationToken.None);

        Assert.Equal("42", result.Output);
    }

    [Fact]
    public async Task RunAsync_Should_Put_The_Child_Span_Under_The_Parent()
    {
        await this.CreateRunner().RunAsync(
            "adder", traceId, parentSpanId, TimeSpan.FromSeconds(5),
            _ => Task.FromResult("42"), CancellationToken.None);

        Assert.All(this.appended, item => Assert.Equal(parentSpanId, item.ParentSpanId));
    }

    [Fact]
    public async Task RunAsync_Should_Emit_Started_Then_Completed_In_The_Same_Span()
    {
        await this.CreateRunner().RunAsync(
            "adder", traceId, parentSpanId, TimeSpan.FromSeconds(5),
            _ => Task.FromResult("42"), CancellationToken.None);

        Assert.Equal(
            [ExecutionEventType.WorkerStarted, ExecutionEventType.WorkerCompleted],
            this.appended.Select(item => item.Type).ToList());
    }

    [Fact]
    public async Task RunAsync_Should_Record_A_Failure_Instead_Of_Throwing()
    {
        var result = await this.CreateRunner().RunAsync(
            "faulty", traceId, parentSpanId, TimeSpan.FromSeconds(5),
            _ => throw new InvalidOperationException("the tool broke"), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_Should_Emit_WorkerFailed_When_The_Work_Throws()
    {
        await this.CreateRunner().RunAsync(
            "faulty", traceId, parentSpanId, TimeSpan.FromSeconds(5),
            _ => throw new InvalidOperationException("the tool broke"), CancellationToken.None);

        Assert.Equal(ExecutionEventType.WorkerFailed, this.appended[^1].Type);
    }

    [Fact]
    public async Task RunAsync_Should_Fail_A_Worker_That_Overruns_Its_Bound()
    {
        var result = await this.CreateRunner().RunAsync(
            "slow", traceId, parentSpanId, TimeSpan.FromMilliseconds(50),
            async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return "never";
            },
            CancellationToken.None);

        Assert.Contains("bound", result.Output, StringComparison.Ordinal);
    }

    private WorkerRunner CreateRunner()
    {
        return new WorkerRunner(
            this.eventStore, new FakeTimeProvider(now), NullLogger<WorkerRunner>.Instance);
    }
}
