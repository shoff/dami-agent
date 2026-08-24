namespace Dami.Contracts.Capabilities;

/// <summary>Accepts version-pinned skill lifecycle changes through durable write-ahead.</summary>
public interface ISkillLifecycleService
{
    /// <summary>Durably accepts, materializes, and journals one skill change.</summary>
    Task<SkillChangeRecord> ApplyAsync(
        SkillChangeRequest request,
        string diff,
        CancellationToken cancellationToken);
}
