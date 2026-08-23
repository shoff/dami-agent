namespace Dami.Capabilities;

/// <summary>Identifies whether capability-provided content may be trusted as instructions.</summary>
public enum TrustLevel
{
    /// <summary>Content is treated as untrusted data.</summary>
    Untrusted,

    /// <summary>Content is approved for use as instructions.</summary>
    Trusted,
}
