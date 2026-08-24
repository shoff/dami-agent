namespace Dami.Capabilities.Native;

/// <summary>Looks up activated native implementations without exposing mutation.</summary>
public interface INativeCapabilityCatalog
{
    /// <summary>Finds the handler registered under a stable capability identifier.</summary>
    INativeCapabilityHandler? Find(Guid capabilityId);
}
