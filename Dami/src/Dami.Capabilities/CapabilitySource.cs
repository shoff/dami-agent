namespace Dami.Capabilities;

/// <summary>Identifies where a normalized capability originated.</summary>
public enum CapabilitySource
{
    /// <summary>An in-process C# plugin.</summary>
    Native,

    /// <summary>An out-of-process Model Context Protocol server.</summary>
    Mcp,

    /// <summary>A progressively disclosed skill directory.</summary>
    Skill,
}
