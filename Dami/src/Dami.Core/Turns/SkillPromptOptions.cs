namespace Dami.Core.Turns;

/// <summary>Hard limits for progressively disclosed skill prompt content.</summary>
public sealed class SkillPromptOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "SkillPrompt";

    /// <summary>Gets or sets the maximum rendered skill-section characters.</summary>
    public int MaxPromptCharacters { get; set; } = 8_000;

    /// <summary>Gets or sets the maximum selected skills disclosed in one turn.</summary>
    public int MaxSkills { get; set; } = 8;
}
