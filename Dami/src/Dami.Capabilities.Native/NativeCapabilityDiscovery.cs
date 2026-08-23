using System.Reflection;

namespace Dami.Capabilities.Native;

/// <summary>Finds attribute-declared native tools in managed assemblies.</summary>
public sealed class NativeCapabilityDiscovery : INativeCapabilityDiscovery
{
    /// <inheritdoc />
    public IReadOnlyList<NativeCapabilityRegistration> Discover(
        Assembly assembly,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var registrations = new List<NativeCapabilityRegistration>();

        foreach (var type in assembly.DefinedTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var metadata = type.GetCustomAttribute<NativeCapabilityAttribute>();
            if (metadata is null || type.IsAbstract)
            {
                continue;
            }

            var entry = new CapabilityEntry(
                Guid.Parse(metadata.CapabilityId),
                metadata.Name,
                metadata.Description,
                CapabilityKind.Tool,
                CapabilitySource.Native,
                TrustLevel.Trusted,
                metadata.Tags,
                metadata.SchemaReference,
                null,
                [],
                metadata.Version,
                registeredAt);
            registrations.Add(new NativeCapabilityRegistration(type.AsType(), entry));
        }

        return Array.AsReadOnly(registrations.ToArray());
    }
}
