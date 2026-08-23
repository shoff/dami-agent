using Dami.Contracts.Proactive;

namespace Dami.Proactive;

/// <summary>Identifies one completed pass and how it ended.</summary>
public readonly record struct ProactivePassOutcome(Guid TraceId, ProactiveStatus Status);
