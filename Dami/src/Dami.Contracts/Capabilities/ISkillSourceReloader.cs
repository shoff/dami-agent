namespace Dami.Contracts.Capabilities;

/// <summary>Atomically reloads the complete filesystem-skill registry source.</summary>
public interface ISkillSourceReloader
{
    /// <summary>Reloads and publishes the current source snapshot.</summary>
    Task ReloadAsync(DateTimeOffset registeredAt, CancellationToken cancellationToken);
}
