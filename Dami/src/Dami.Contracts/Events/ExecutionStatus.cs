namespace Dami.Contracts.Events;

/// <summary>The state of the operation an event describes.</summary>
public enum ExecutionStatus
{
    /// <summary>Accepted, not yet started.</summary>
    Queued = 0,

    /// <summary>Started and still running.</summary>
    Running = 1,

    /// <summary>Finished, and the result is trustworthy.</summary>
    Succeeded = 2,

    /// <summary>Finished, and it did not do what was asked.</summary>
    Failed = 3,

    /// <summary>Stopped deliberately before completion.</summary>
    Cancelled = 4,

    /// <summary>Blocked pending an approval or a clarification.</summary>
    Waiting = 5,
}
