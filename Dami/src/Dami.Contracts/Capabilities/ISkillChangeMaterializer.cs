namespace Dami.Contracts.Capabilities;

/// <summary>Converges one durable skill change into its filesystem postcondition.</summary>
public interface ISkillChangeMaterializer
{
    /// <summary>Applies one version-pinned change idempotently.</summary>
    Task ApplyAsync(SkillChangeRecord record, CancellationToken cancellationToken);
}
