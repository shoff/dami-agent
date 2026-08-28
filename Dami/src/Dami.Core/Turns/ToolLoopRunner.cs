using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;

namespace Dami.Core.Turns;

/// <summary>Runs a bounded provider-neutral model/tool conversation.</summary>
public sealed class ToolLoopRunner : IToolLoopRunner
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
        PrivacyClass privacy,
        ExecutionOrigin origin,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(toolSchemas);
        ValidateProvenance(privacy, origin);
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
                traceId, parentSpanId, privacy, origin, turn, cancellationToken)
                .ConfigureAwait(false));
        }
    }

    private static void ValidateProvenance(PrivacyClass privacy, ExecutionOrigin origin)
    {
        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
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
        PrivacyClass privacy,
        ExecutionOrigin origin,
        ToolModelTurn turn,
        CancellationToken cancellationToken)
    {
        var invocation = turn.Invocation
            ?? throw new InvalidDataException("Tool model turn contains neither an answer nor an invocation.");
        var callId = turn.CallId
            ?? throw new InvalidDataException("Tool model turn is missing its call identifier.");
        var spanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, spanId, parentSpanId, origin, invocation, callId,
            ExecutionEventType.ToolRequested, ExecutionStatus.Queued, cancellationToken).ConfigureAwait(false);
        return await this.ExecuteStartedAsync(
            traceId, spanId, parentSpanId, privacy, origin,
            invocation, callId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a tool that did not produce a result, whatever ended it.</summary>
    private Task FailAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        ExecutionOrigin origin,
        CapabilityInvocation invocation,
        string callId,
        ExecutionStatus status)
    {
        return this.EmitAsync(
            traceId, spanId, parentSpanId, origin, invocation, callId,
            ExecutionEventType.ToolFailed, status, CancellationToken.None);
    }

    /// <summary>Runs one tool call and returns its outcome, successful or not.</summary>
    /// <remarks>
    /// A failed tool is a turn in the conversation, not the end of it. <c>ToolFailed</c>
    /// is recorded exactly as it always was — the audit trail is unchanged — and the
    /// reason is then handed back so the model can correct itself inside the call bound it
    /// already has. Rethrowing here meant one bad argument from a small model killed the
    /// whole turn: a local 8B asking to read a file at the literal path <c>"path"</c> took
    /// down an entire advisory run. Cancellation is different and still propagates: that
    /// is the caller's decision, not something the model can recover from.
    /// </remarks>
    private async Task<ToolExecutionExchange> ExecuteStartedAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        PrivacyClass privacy,
        ExecutionOrigin origin,
        CapabilityInvocation invocation,
        string callId,
        CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            traceId, spanId, parentSpanId, origin, invocation, callId,
            ExecutionEventType.ToolStarted, ExecutionStatus.Running, cancellationToken).ConfigureAwait(false);
        CapabilityExecutionResult result;
        try
        {
            var request = new CapabilityExecutionRequest(
                traceId, spanId, privacy, origin, invocation);
            result = await this.executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.FailAsync(
                traceId, spanId, parentSpanId, origin, invocation, callId,
                ExecutionStatus.Cancelled).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await this.FailAsync(
                traceId, spanId, parentSpanId, origin, invocation, callId,
                ExecutionStatus.Failed).ConfigureAwait(false);
            return ToolExecutionExchange.Failed(callId, invocation, exception.Message);
        }

        await this.EmitAsync(
            traceId, spanId, parentSpanId, origin, invocation, callId,
            ExecutionEventType.ToolCompleted, ExecutionStatus.Succeeded, cancellationToken).ConfigureAwait(false);
        return new ToolExecutionExchange(callId, invocation, result);
    }

    private Task<long> EmitAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        ExecutionOrigin origin,
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
            origin, ACTOR, type, status,
            this.clock.GetUtcNow(), $"tool {invocation.CapabilityId}: {type}", metadata: metadata);
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
