using System.Reflection;

namespace Dami.Capabilities.Native;

/// <summary>Publishes discovered native tool metadata to the source-neutral registry.</summary>
public sealed class NativeCapabilityLoader
{
    private readonly INativeCapabilityDiscovery discovery;
    private readonly ICapabilityRegistrar registrar;

    /// <summary>Creates the registration handoff.</summary>
    public NativeCapabilityLoader(
        INativeCapabilityDiscovery discovery,
        ICapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(registrar);
        this.discovery = discovery;
        this.registrar = registrar;
    }

    /// <summary>Discovers native tools and publishes their normalized metadata.</summary>
    public IReadOnlyList<NativeCapabilityRegistration> Load(
        Assembly assembly,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        IReadOnlyList<NativeCapabilityRegistration> registrations = this.discovery
            .Discover(assembly, registeredAt);

        foreach (NativeCapabilityRegistration registration in registrations)
        {
            this.registrar.Register(registration.Entry);
        }

        return registrations;
    }
}
