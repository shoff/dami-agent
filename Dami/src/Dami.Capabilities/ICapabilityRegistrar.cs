namespace Dami.Capabilities;

/// <summary>Accepts normalized capabilities from discovery sources.</summary>
public interface ICapabilityRegistrar
{
    /// <summary>Registers a capability under its stable identifier.</summary>
    /// <param name="entry">The normalized capability registration.</param>
    void Register(CapabilityEntry entry);
}
