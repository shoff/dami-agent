using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.Models;

namespace Dami.Core.Turns;

/// <summary>Runs a bounded provider-neutral model/tool conversation.</summary>
public sealed class ToolLoopRunner
{
    private const string ACTOR = "runtime";

    private readonly IToolCallingChatClient model;
    private readonly ICapabilityExecutor executor;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly int maxToolCalls;

    /// <summary>Creates the tool-loop runner.</summary>
    public ToolLoopRunner(
        IToolCallingChatClient model,
        ICapabilityExecutor executor,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ToolLoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxToolCalls, 0);
        this.model = model;
        this.executor = executor;
        this.eventStore = eventStore;
        this.clock = clock;
        this.maxToolCalls = options.MaxToolCalls;
    }

    /// <summary>Runs until the model answers or exceeds the configured tool-call bound.</summary>
    public async Task<string> RunAsync(
        Guid traceId,
        Guid parentSpanId,
        string prompt,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(toolSchemas);
        var schemaSnapshot = Snapshot(toolSchemas);
        var exchanges = new List<ToolExecutionExchange>();
        while (true)
        {
            var turn = await this.model
                .NextAsync(prompt, schemaSnapshot, Snapshot(exchanges), cancellationToken).ConfigureAwait(false);
            if (turn.Answer is { } answer)
            {
                return answer;
            }

            if (exchanges.Count == this.maxToolCalls)
            {
                throw new InvalidOperationException(
                    $"Tool loop exceeded its bound of {this.maxToolCalls} calls.");
            }

            exchanges.Add(await this.ExecuteAsync(
                traceId, parentSpanId, turn, cancellationToken).ConfigureAwait(false));
        }
    }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
    {
        return source.Count == 0
            ? Array.Empty<T>()
            : Array.AsReadOnly(source.ToArray());
    }

    private async Task<ToolExecutionExchange> ExecuteAsync(
        Guid traceId,
        Guid parentSpanId,
        ToolModelTurn turn,
        CancellationToken cancellationToken)
    {
        var invocation = turn.Invocation
            ?? throw new InvalidDataException("Tool model turn contains neither an answer nor an invocation.");
        var callId = turn.CallId
            ?? throw new InvalidDataException("Tool model turn is missing its call identifier.");
        var spanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, spanId, parentSpanId, invocation, callId,
            ExecutionEventType.ToolRequested, ExecutionStatus.Queued, cancellationToken).ConfigureAwait(false);
        return await this.ExecuteStartedAsync(
            traceId, spanId, parentSpanId, invocation, callId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolExecutionExchange> ExecuteStartedAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        CapabilityInvocation invocation,
        string callId,
        CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            traceId, spanId, parentSpanId, invocation, callId,
            ExecutionEventType.ToolStarted, ExecutionStatus.Running, cancellationToken).ConfigureAwait(false);
        CapabilityExecutionResult result;
        try
        {
            var request = new CapabilityExecutionRequest(traceId, spanId, invocation);
            result = await this.executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.EmitAsync(
                traceId, spanId, parentSpanId, invocation, callId,
                ExecutionEventType.ToolFailed, ExecutionStatus.Cancelled, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await this.EmitAsync(
                traceId, spanId, parentSpanId, invocation, callId,
                ExecutionEventType.ToolFailed, ExecutionStatus.Failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await this.EmitAsync(
            traceId, spanId, parentSpanId, invocation, callId,
            ExecutionEventType.ToolCompleted, ExecutionStatus.Succeeded, cancellationToken).ConfigureAwait(false);
        return new ToolExecutionExchange(callId, invocation, result);
    }

    private Task<long> EmitAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        CapabilityInvocation invocation,
        string callId,
        ExecutionEventType type,
        ExecutionStatus status,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["call_id"] = callId,
            ["capability_id"] = invocation.CapabilityId.ToString(),
        };
        var executionEvent = new ExecutionEvent(
            Guid.NewGuid(), traceId, spanId, parentSpanId,
            ExecutionOrigin.UserTurn, ACTOR, type, status,
            this.clock.GetUtcNow(), $"tool {invocation.CapabilityId}: {type}", metadata: metadata);
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
