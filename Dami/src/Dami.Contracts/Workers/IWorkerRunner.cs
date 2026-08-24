namespace Dami.Contracts.Workers;

/// <summary>Runs one bounded unit of work as a child span of an existing trace.</summary>
/// <remarks>
/// The charter's worker loop: Dami Core → bounded worker → child trace → evidence →
/// Dami Core. The runner owns the discipline — start/finish events under the parent
/// span, a hard time bound, failures recorded rather than thrown past the trace —
/// so individual workers stay plain functions.
/// </remarks>
public interface IWorkerRunner
{
    /// <summary>Runs the work under a new child span. Never throws for worker failure.</summary>
    Task<WorkerResult> RunAsync(
        string workerName,
        Guid traceId,
        Guid parentSpanId,
        TimeSpan bound,
        Func<CancellationToken, Task<string>> work,
        CancellationToken cancellationToken);
}
