namespace Dami.Contracts.Memory;

/// <summary>How often Dami challenged something in a window, and how it landed.</summary>
/// <remarks>
/// The quarterly review instrument from D-011. <see cref="Total"/> falling over
/// successive windows is direct evidence the tuning loop is eating the auditor. A high
/// <see cref="Accepted"/> share is not reassurance — challenges that are always accepted
/// may simply be the safe ones.
/// </remarks>
public sealed record PushbackRate(
    DateTimeOffset From,
    DateTimeOffset To,
    int Total,
    int Accepted,
    int Rejected,
    int Deferred,
    int Unresolved);
