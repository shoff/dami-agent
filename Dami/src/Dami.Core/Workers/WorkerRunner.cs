using Dami.Contracts.Events;
using Dami.Contracts.Workers;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Workers;

/// <summary>The one implementation of the worker discipline.</summary>
public sealed class WorkerRunner : IWorkerRunner
{
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<WorkerRunner> logger;

    /// <summary>Creates the runner.</summary>
    public WorkerRunner(
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<WorkerRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkerResult> RunAsync(
        string workerName,
        Guid traceId,
        Guid parentSpanId,
        TimeSpan bound,
        Func<CancellationToken, Task<string>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerName);
        ArgumentNullException.ThrowIfNull(work);

        var spanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, spanId, parentSpanId, workerName, ExecutionEventType.WorkerStarted,
            ExecutionStatus.Running, $"{workerName} started (bound {bound.TotalSeconds:F0}s)",
            cancellationToken).ConfigureAwait(false);

        return await this.RunBoundedAsync(
            workerName, traceId, spanId, parentSpanId, bound, work, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WorkerResult> RunBoundedAsync(
        string workerName,
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        TimeSpan bound,
        Func<CancellationToken, Task<string>> work,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(bound);
        try
        {
            var output = await work(bounded.Token).ConfigureAwait(false);
            await this.EmitAsync(
                traceId, spanId, parentSpanId, workerName, ExecutionEventType.WorkerCompleted,
                ExecutionStatus.Succeeded, $"{workerName} returned {output.Length} chars",
                cancellationToken).ConfigureAwait(false);
            return new WorkerResult(spanId, workerName, true, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            var reason = exception is OperationCanceledException
                ? $"overran its bound of {bound.TotalSeconds:F0}s"
                : exception.Message;
            await this.EmitAsync(
                traceId, spanId, parentSpanId, workerName, ExecutionEventType.WorkerFailed,
                ExecutionStatus.Failed, $"{workerName}: {reason}", cancellationToken)
                .ConfigureAwait(false);
            this.logger.LogWarning("Worker {Worker} failed: {Reason}", workerName, reason);
            return new WorkerResult(spanId, workerName, false, reason);
        }
    }

    private Task EmitAsync(
        Guid traceId,
        Guid spanId,
        Guid parentSpanId,
        string workerName,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            Guid.NewGuid(), traceId, spanId, parentSpanId,
            ExecutionOrigin.UserTurn, $"worker:{workerName}", type, status,
            this.clock.GetUtcNow(), label);
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
