using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive;

/// <summary>Runs one proactive pass and routes everything it produced.</summary>
/// <remarks>
/// The runner, not the service, writes to the ledger, the queue, and the event stream —
/// so a service cannot bypass the cap, skip provenance, or leave the trace incomplete.
/// Every event carries <see cref="ExecutionOrigin.ScheduledService"/>, which is what
/// keeps the proactive half of the system visible in the graph (D-018).
///
/// A throwing service is contained: the failure is recorded as a
/// <see cref="ExecutionEventType.TraceFailed"/> event and returned as a status. It is
/// never rethrown, because one broken pass must not take the tier down with it (§3.1).
/// </remarks>
public sealed class ProactivePassRunner
{
    private readonly IExecutionEventStore eventStore;
    private readonly IConclusionLedger conclusionLedger;
    private readonly ISurfacingQueue surfacingQueue;
    private readonly TimeProvider clock;
    private readonly ILogger<ProactivePassRunner> logger;

    /// <summary>Creates the runner.</summary>
    public ProactivePassRunner(
        IExecutionEventStore eventStore,
        IConclusionLedger conclusionLedger,
        ISurfacingQueue surfacingQueue,
        TimeProvider clock,
        ILogger<ProactivePassRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(conclusionLedger);
        ArgumentNullException.ThrowIfNull(surfacingQueue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.eventStore = eventStore;
        this.conclusionLedger = conclusionLedger;
        this.surfacingQueue = surfacingQueue;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Runs one pass of one service.</summary>
    public async Task<ProactivePassOutcome> RunAsync(
        IProactiveService service,
        DateTimeOffset? lastRanAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        var traceId = Guid.NewGuid();
        var context = new ProactiveContext(traceId, this.clock.GetUtcNow(), lastRanAt);

        await this.EmitAsync(
            traceId, service.ServiceName, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            $"{service.ServiceName} pass started", cancellationToken).ConfigureAwait(false);

        try
        {
            return await this.ExecuteAsync(service, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await this.RecordCancelledAsync(traceId, service.ServiceName).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await this.RecordFailedAsync(traceId, service.ServiceName, exception).ConfigureAwait(false);
        }
    }

    private async Task<ProactivePassOutcome> RecordCancelledAsync(Guid traceId, string serviceName)
    {
        // CancellationToken.None deliberately: the pass's token is already cancelled, and
        // the record of the cancellation must still be written.
        await this.EmitAsync(
            traceId, serviceName, ExecutionEventType.TraceCancelled, ExecutionStatus.Cancelled,
            $"{serviceName} pass cancelled", CancellationToken.None).ConfigureAwait(false);
        return new ProactivePassOutcome(traceId, ProactiveStatus.Cancelled);
    }

    private async Task<ProactivePassOutcome> RecordFailedAsync(Guid traceId, string serviceName, Exception exception)
    {
        this.logger.LogError(
            exception, "Proactive pass {ServiceName} failed in trace {TraceId}", serviceName, traceId);

        await this.EmitAsync(
            traceId, serviceName, ExecutionEventType.TraceFailed, ExecutionStatus.Failed,
            $"{serviceName} pass failed: {exception.Message}", CancellationToken.None).ConfigureAwait(false);
        return new ProactivePassOutcome(traceId, ProactiveStatus.Failed);
    }

    private async Task<ProactivePassOutcome> ExecuteAsync(
        IProactiveService service,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        var result = await service.RunPassAsync(context, cancellationToken).ConfigureAwait(false);
        await this.RouteAsync(context.TraceId, service.ServiceName, result, cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            context.TraceId, service.ServiceName, ExecutionEventType.TraceCompleted, ExecutionStatus.Succeeded,
            result.Note.Length > 0
                ? $"{service.ServiceName}: {result.Note}"
                : $"{service.ServiceName}: {result.Conclusions.Count} concluded, {result.Surfacings.Count} surfaced",
            cancellationToken).ConfigureAwait(false);

        return new ProactivePassOutcome(context.TraceId, result.Status);
    }

    private async Task RouteAsync(
        Guid traceId,
        string serviceName,
        ProactiveResult result,
        CancellationToken cancellationToken)
    {
        foreach (var conclusion in result.Conclusions)
        {
            await this.conclusionLedger.RecordAsync(conclusion, cancellationToken).ConfigureAwait(false);
            await this.EmitAsync(
                traceId, serviceName, ExecutionEventType.ConclusionRecorded, ExecutionStatus.Succeeded,
                conclusion.Statement, cancellationToken).ConfigureAwait(false);
        }

        foreach (var surfacing in result.Surfacings)
        {
            var accepted = await this.surfacingQueue
                .EnqueueAsync(surfacing, cancellationToken).ConfigureAwait(false);

            // A suppressed surfacing gets no Surfaced event: the stream records what
            // reached for Steve's attention, and this one was stopped by the cap. The
            // suppression itself is durable in the queue table.
            if (accepted)
            {
                await this.EmitAsync(
                    traceId, serviceName, ExecutionEventType.Surfaced, ExecutionStatus.Succeeded,
                    surfacing.Title, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task<long> EmitAsync(
        Guid traceId,
        string serviceName,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            eventId: Guid.NewGuid(),
            traceId: traceId,
            spanId: traceId,
            parentSpanId: null,
            origin: ExecutionOrigin.ScheduledService,
            actorId: serviceName,
            type: type,
            status: status,
            occurredAt: this.clock.GetUtcNow(),
            label: label);

        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
