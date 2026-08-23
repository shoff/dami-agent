namespace Dami.Contracts.Memory;

/// <summary>The record of every challenge Dami made, and what came of it.</summary>
public interface IPushbackLedger
{
    /// <summary>Records a challenge at the moment it is made.</summary>
    Task RecordAsync(PushbackRecord pushback, CancellationToken cancellationToken);

    /// <summary>Records how a challenge landed, once that is known.</summary>
    Task ResolveAsync(
        Guid pushbackId,
        PushbackOutcome outcome,
        string? followUpNote,
        CancellationToken cancellationToken);

    /// <summary>Counts challenges in a window, by outcome.</summary>
    Task<PushbackRate> RateAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Every challenge in a window, oldest first.</summary>
    IAsyncEnumerable<PushbackRecord> BetweenAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
