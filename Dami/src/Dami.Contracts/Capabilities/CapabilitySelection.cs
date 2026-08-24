namespace Dami.Contracts.Capabilities;

/// <summary>The bounded tools and deferred skills selected for one turn.</summary>
public sealed class CapabilitySelection
{
    /// <summary>Creates an immutable turn capability selection.</summary>
    public CapabilitySelection(
        IReadOnlyList<CapabilityToolSchema> tools,
        IReadOnlyList<SkillSelection> skills)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(skills);
        this.Tools = Array.AsReadOnly(tools.ToArray());
        this.Skills = Array.AsReadOnly(skills.ToArray());
    }

    /// <summary>Gets the selected tool schemas.</summary>
    public IReadOnlyList<CapabilityToolSchema> Tools { get; }

    /// <summary>Gets the selected skill references, still without body content.</summary>
    public IReadOnlyList<SkillSelection> Skills { get; }
}
