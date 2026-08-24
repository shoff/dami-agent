namespace Dami.Capabilities.Native;

/// <summary>Registers activated native implementations.</summary>
public interface INativeCapabilityRegistrar
{
    /// <summary>Registers one handler under its stable capability identifier.</summary>
    void Register(Guid capabilityId, INativeCapabilityHandler handler);
}
