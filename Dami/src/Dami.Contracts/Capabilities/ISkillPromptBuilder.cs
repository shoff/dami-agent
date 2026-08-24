namespace Dami.Contracts.Capabilities;

/// <summary>Builds the bounded prompt section for selected procedural skills.</summary>
public interface ISkillPromptBuilder
{
    /// <summary>Loads and renders only the selected skill bodies.</summary>
    Task<string> BuildAsync(
        IReadOnlyList<SkillSelection> skills,
        CancellationToken cancellationToken);
}
