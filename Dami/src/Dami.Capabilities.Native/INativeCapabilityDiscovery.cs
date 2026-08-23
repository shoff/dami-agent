using System.Reflection;

namespace Dami.Capabilities.Native;

/// <summary>Discovers native tool metadata without activating implementation types.</summary>
public interface INativeCapabilityDiscovery
{
    /// <summary>Finds annotated concrete types in an assembly.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="registeredAt">The timestamp assigned to the registry entries.</param>
    /// <returns>Normalized native capability registrations.</returns>
    IReadOnlyList<NativeCapabilityRegistration> Discover(
        Assembly assembly,
        DateTimeOffset registeredAt);
}
