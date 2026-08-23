namespace Dami.Capabilities;

/// <summary>Identifies how a capability affects an agent turn.</summary>
public enum CapabilityKind
{
    /// <summary>Executable code invoked through a typed schema.</summary>
    Tool,

    /// <summary>Procedural knowledge loaded into context.</summary>
    Skill,

    /// <summary>A named set of tools and skills selected together.</summary>
    Bundle,
}
