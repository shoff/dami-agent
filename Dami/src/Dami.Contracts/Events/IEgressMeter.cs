namespace Dami.Contracts.Events;

/// <summary>Reads egress attempt counts from the event stream.</summary>
/// <remarks>
/// D-012 makes every egress attempt a durable <see cref="ExecutionEventType.EgressRequested"/>
/// event before any gate runs, so the stream is already the meter — no separate counter
/// to keep consistent, and refused attempts count, which is exactly right for detecting
/// a runaway loop that keeps trying.
/// </remarks>
public interface IEgressMeter
{
    /// <summary>How many egress attempts were recorded at or after <paramref name="since"/>.</summary>
    Task<int> CountRequestsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
