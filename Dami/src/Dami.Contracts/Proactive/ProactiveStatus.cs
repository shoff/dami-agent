namespace Dami.Contracts.Proactive;

/// <summary>How a proactive pass ended.</summary>
public enum ProactiveStatus
{
    /// <summary>Ran to completion. Says nothing about whether anything was concluded.</summary>
    Completed = 0,

    /// <summary>Ran and failed. The scheduler records it and the next cadence tries again.</summary>
    Failed = 1,

    /// <summary>Stopped by cancellation before finishing.</summary>
    Cancelled = 2,
}
