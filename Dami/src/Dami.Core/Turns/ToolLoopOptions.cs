namespace Dami.Core.Turns;

/// <summary>Safety bounds for one model/tool conversation loop.</summary>
public sealed class ToolLoopOptions
{
    /// <summary>Gets or sets the maximum number of tool calls in one turn.</summary>
    public int MaxToolCalls { get; set; } = 8;
}
