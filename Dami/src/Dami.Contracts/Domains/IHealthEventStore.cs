namespace Dami.Contracts.Domains;

/// <summary>Storage for the health domain (K2). LocalOnly — no method here egresses.</summary>
public interface IHealthEventStore
{
    /// <summary>Stores a health event. Idempotent on (observation, description).</summary>
    Task RecordAsync(HealthEvent healthEvent, CancellationToken cancellationToken);

    /// <summary>Observations not yet examined by the health collector, oldest first.</summary>
    IAsyncEnumerable<(Guid ObservationId, DateOnly OccurredOn, string Body)> UnexaminedAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Marks an observation examined even when it yielded no health facts, so the
    /// collector does not re-read it every pass.</summary>
    Task MarkExaminedAsync(Guid observationId, CancellationToken cancellationToken);

    /// <summary>The health timeline in date order — the shape D-007's cross-domain join reads.</summary>
    IAsyncEnumerable<HealthEvent> TimelineAsync(int limit, CancellationToken cancellationToken);
}
