namespace Dami.Contracts.Transport;

/// <summary>Describes protocol-level handling required for a transport frame.</summary>
[Flags]
public enum FrameFlags : byte
{
    /// <summary>The frame has no special handling requirements.</summary>
    None = 0,

    /// <summary>The frame completes a correlated stream.</summary>
    EndOfStream = 1,

    /// <summary>The frame reports a failure for a correlated operation.</summary>
    Error = 2
}
