namespace Dami.Core.Turns;

/// <summary>Safety bounds for one model/tool conversation loop.</summary>
public sealed class ToolLoopOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "ToolLoop";

    /// <summary>Gets or sets the maximum number of tool calls in one turn.</summary>
    public int MaxToolCalls { get; set; } = 8;
}
