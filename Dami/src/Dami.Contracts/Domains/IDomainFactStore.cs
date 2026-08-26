namespace Dami.Contracts.Domains;

/// <summary>One dated, one-clause fact in a domain, with where it came from.</summary>
/// <param name="FactId">Stable id.</param>
/// <param name="Domain">The domain name, lower-case: <c>network</c>, <c>civic</c>, …</param>
/// <param name="AsOf">The day the fact was true or observed.</param>
/// <param name="Category">A short domain-defined kind: <c>interface</c>, <c>reachability</c>, …</param>
/// <param name="Description">The fact, one clause, checkable.</param>
/// <param name="Source">What produced it: a collector name, a feed, a file.</param>
/// <param name="RecordedAt">When it was written.</param>
public sealed record DomainFact(
    Guid FactId,
    string Domain,
    DateOnly AsOf,
    string Category,
    string Description,
    string Source,
    DateTimeOffset RecordedAt);

/// <summary>The shared store for every domain after health (K4).</summary>
public interface IDomainFactStore
{
    /// <summary>Records a fact; false when the same statement is already recorded for that day.</summary>
    Task<bool> RecordAsync(DomainFact fact, CancellationToken cancellationToken);

    /// <summary>Facts newest first, in one domain or, with null, across all, rejections excluded.</summary>
    IAsyncEnumerable<DomainFact> TimelineAsync(string? domain, int limit, CancellationToken cancellationToken);

    /// <summary>Facts in one domain dated within a window, soonest first, rejections excluded.</summary>
    IAsyncEnumerable<DomainFact> BetweenAsync(
        string domain, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);

    /// <summary>Rejects a wrong fact permanently. False when the id is unknown.</summary>
    Task<bool> RejectAsync(Guid factId, string reason, CancellationToken cancellationToken);

    /// <summary>Every domain that holds at least one fact, with its count.</summary>
    Task<IReadOnlyList<(string Domain, int Facts)>> DomainsAsync(CancellationToken cancellationToken);
}
