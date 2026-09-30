namespace Dami.Contracts.Runtime;

/// <summary>A pause in force: until when (null: until resumed), why, and since when.</summary>
public sealed record PauseState(DateTimeOffset? Until, string Reason, DateTimeOffset SetAt);

/// <summary>
/// The switch that stops everything Dami starts on her own — proactive passes, scheduled jobs,
/// the check-in. Replies to Steve and the outage alarm are not affected.
/// </summary>
public interface IPauseSwitch
{
    /// <summary>The pause in force at <paramref name="now"/>, or null.</summary>
    Task<PauseState?> CurrentAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Pauses until <paramref name="until"/>, or until resumed when null.</summary>
    Task PauseAsync(DateTimeOffset? until, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Lifts any pause.</summary>
    Task ResumeAsync(CancellationToken cancellationToken);
}
