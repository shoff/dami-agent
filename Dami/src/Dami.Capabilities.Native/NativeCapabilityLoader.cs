using System.Reflection;

namespace Dami.Capabilities.Native;

/// <summary>Publishes discovered native tool metadata to the source-neutral registry.</summary>
public sealed class NativeCapabilityLoader
{
    private readonly INativeCapabilityDiscovery discovery;
    private readonly ICapabilityRegistrar registrar;
    private readonly ICapabilityToolSchemaRegistrar schemaRegistrar;

    /// <summary>Creates the registration handoff.</summary>
    public NativeCapabilityLoader(
        INativeCapabilityDiscovery discovery,
        ICapabilityRegistrar registrar,
        ICapabilityToolSchemaRegistrar schemaRegistrar)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(schemaRegistrar);
        this.discovery = discovery;
        this.registrar = registrar;
        this.schemaRegistrar = schemaRegistrar;
    }

    /// <summary>Discovers native tools and publishes their normalized metadata.</summary>
    public IReadOnlyList<NativeCapabilityRegistration> Load(
        Assembly assembly,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        IReadOnlyList<NativeCapabilityRegistration> registrations = this.discovery
            .Discover(assembly, registeredAt);

        this.Publish(registrations);
        return registrations;
    }

    /// <summary>Publishes an already-filtered discovery set.</summary>
    public void Publish(IReadOnlyList<NativeCapabilityRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (NativeCapabilityRegistration registration in registrations)
        {
            this.registrar.Register(registration.Entry);
            this.schemaRegistrar.Register(registration.Schema);
        }
    }
}
