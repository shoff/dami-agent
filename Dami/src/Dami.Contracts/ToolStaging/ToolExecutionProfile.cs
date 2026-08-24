namespace Dami.Contracts.ToolStaging;

/// <summary>Declares the maximum authority a proposed v1 tool may request.</summary>
public enum ToolExecutionProfile
{
    /// <summary>The tool transforms supplied values without reading local state.</summary>
    PureComputation = 0,

    /// <summary>The tool may read through explicitly supplied local abstractions.</summary>
    ReadOnly = 1,
}
