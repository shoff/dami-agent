namespace Dami.Contracts.Capabilities;

/// <summary>The lifecycle transition requested for one procedural skill.</summary>
public enum SkillChangeKind
{
    /// <summary>Create a skill that does not exist.</summary>
    Author = 0,

    /// <summary>Replace the exact expected skill version.</summary>
    Revise = 1,

    /// <summary>Retire the exact expected skill version.</summary>
    Retire = 2,
}
