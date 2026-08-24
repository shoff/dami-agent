namespace Dami.Contracts.Workers;

/// <summary>What a bounded worker handed back to its parent (charter §7).</summary>
/// <remarks>
/// The evidence is not the string — it is the child span in the event stream, which
/// this result points at. The parent renders or stores the output; the trace proves
/// what happened.
/// </remarks>
public sealed record WorkerResult
{
    /// <summary>Creates a result.</summary>
    public WorkerResult(Guid spanId, string workerName, bool succeeded, string output)
    {
        ArgumentNullException.ThrowIfNull(workerName);
        ArgumentNullException.ThrowIfNull(output);

        this.SpanId = spanId;
        this.WorkerName = workerName;
        this.Succeeded = succeeded;
        this.Output = output;
    }

    /// <summary>The child span the worker ran under — the evidence lives there.</summary>
    public Guid SpanId { get; }

    /// <summary>Which worker ran.</summary>
    public string WorkerName { get; }

    /// <summary>Whether it completed inside its bound.</summary>
    public bool Succeeded { get; }

    /// <summary>The worker's output, or the failure reason.</summary>
    public string Output { get; }
}
