namespace Dami.Contracts.ToolStaging;

/// <summary>Terminal outcome of one attempt to publish an approved tool.</summary>
public enum ToolActivationStatus
{
    /// <summary>The exact verified tool is published in the live registry.</summary>
    Activated = 0,

    /// <summary>Publication failed and did not leave a partial registration.</summary>
    Failed = 1,
}
