using Dami.Contracts.Events;

namespace Dami.Contracts.Code;

/// <summary>A task to carry out on the code, and the turn it belongs to.</summary>
public sealed record CodeChangeRequest
{
    /// <summary>Creates a request.</summary>
    public CodeChangeRequest(string task, Guid traceId, ExecutionOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        this.Task = task;
        this.TraceId = traceId;
        this.Origin = origin;
    }

    /// <summary>What to do, in words. Goes to the coding agent; never into an event label.</summary>
    public string Task { get; }

    /// <summary>The turn this belongs to.</summary>
    public Guid TraceId { get; }

    /// <summary>What kind of work asked for it.</summary>
    public ExecutionOrigin Origin { get; }
}
