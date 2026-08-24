namespace Dami.Contracts.Capabilities;

/// <summary>Durable write-ahead storage for immutable skill changes.</summary>
public interface ISkillChangeStore
{
    /// <summary>Atomically stores the change and its requested execution event.</summary>
    Task CreateAsync(SkillChangeRecord record, CancellationToken cancellationToken);

    /// <summary>Finds one change by its retry-stable identifier.</summary>
    Task<SkillChangeRecord?> FindAsync(Guid changeId, CancellationToken cancellationToken);
}
