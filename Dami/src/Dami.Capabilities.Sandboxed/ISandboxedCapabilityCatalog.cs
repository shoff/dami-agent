namespace Dami.Capabilities.Sandboxed;

/// <summary>Looks up dynamically published sandboxed execution registrations.</summary>
public interface ISandboxedCapabilityCatalog
{
    /// <summary>Finds the exact registration for a stable capability identifier.</summary>
    SandboxedCapabilityRegistration? Find(Guid capabilityId);
}
