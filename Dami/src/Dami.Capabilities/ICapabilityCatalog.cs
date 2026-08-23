namespace Dami.Capabilities;

/// <summary>Looks up normalized capabilities without exposing registry mutation.</summary>
public interface ICapabilityCatalog
{
    /// <summary>Finds a registered capability by its stable identifier.</summary>
    /// <param name="capabilityId">The stable capability identifier.</param>
    /// <returns>The matching registration, or <see langword="null"/>.</returns>
    CapabilityEntry? Find(Guid capabilityId);
}
