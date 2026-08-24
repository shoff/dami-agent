namespace Dami.Contracts.Capabilities;

/// <summary>Materializes and journals one already-durable skill change.</summary>
public interface ISkillChangeProcessor
{
    /// <summary>Converges the filesystem, reloads the registry, and records the outcome.</summary>
    Task ProcessAsync(SkillChangeRecord record, CancellationToken cancellationToken);
}
