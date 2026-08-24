using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Workers;

namespace Dami.Gateway.Cli;

/// <summary>Local vision from the shell — run as a bounded worker under a real trace.</summary>
/// <remarks>
/// The first live instance of the charter's worker loop (acceptance item 6): the
/// caption is produced by a vision worker in a child span; the evidence is in the
/// trace, not this class's word for it.
/// </remarks>
public sealed class VisionCommands
{
    private static readonly TimeSpan bound = TimeSpan.FromMinutes(5);

    private readonly IVisionClient visionClient;
    private readonly IWorkerRunner workerRunner;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public VisionCommands(
        IVisionClient visionClient,
        IWorkerRunner workerRunner,
        IExecutionEventStore eventStore,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(visionClient);
        ArgumentNullException.ThrowIfNull(workerRunner);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.visionClient = visionClient;
        this.workerRunner = workerRunner;
        this.eventStore = eventStore;
        this.clock = clock;
    }

    /// <summary>Captions one image file. The image never leaves the host.</summary>
    public async Task<int> CaptionAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            await Console.Error.WriteLineAsync($"no such file: {path}").ConfigureAwait(false);
            return 1;
        }

        var image = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("looking (local vision model, as a bounded worker)...");
        return await this.CaptionTracedAsync(path, image, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> CaptionTracedAsync(
        string path,
        byte[] image,
        CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        var rootSpanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, rootSpanId, ExecutionEventType.TraceStarted, ExecutionStatus.Running,
            $"caption {Path.GetFileName(path)}", cancellationToken).ConfigureAwait(false);

        var result = await this.workerRunner.RunAsync(
            "vision-caption", traceId, rootSpanId, bound,
            token => this.visionClient.DescribeAsync(
                image, "Caption this image in one sentence, then list 3 short category tags.", token),
            cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            traceId, rootSpanId,
            result.Succeeded ? ExecutionEventType.TraceCompleted : ExecutionEventType.TraceFailed,
            result.Succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
            $"caption {(result.Succeeded ? "done" : "failed")}", cancellationToken).ConfigureAwait(false);

        Console.WriteLine(result.Output);
        Console.WriteLine($"[worker {result.WorkerName} · span {result.SpanId.ToString("N")[..8]} · trace {traceId.ToString("N")[..8]}]");
        return result.Succeeded ? 0 : 1;
    }

    private Task EmitAsync(
        Guid traceId,
        Guid spanId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            Guid.NewGuid(), traceId, spanId, null, ExecutionOrigin.UserTurn,
            "dami-cli", type, status, this.clock.GetUtcNow(), label);
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
