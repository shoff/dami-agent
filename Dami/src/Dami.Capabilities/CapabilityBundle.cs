namespace Dami.Capabilities;

/// <summary>A named set of tools and skills selected together for a turn.</summary>
public sealed class CapabilityBundle
{
    /// <summary>Initializes a capability bundle.</summary>
    /// <param name="name">The bundle name.</param>
    /// <param name="capabilities">The selected tools and skills.</param>
    public CapabilityBundle(string name, IReadOnlyList<CapabilityEntry> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Any(capability => capability.Kind == CapabilityKind.Bundle))
        {
            throw new ArgumentException(
                "A turn-ready bundle may contain tools and skills, not bundle definitions.",
                nameof(capabilities));
        }

        this.Name = name;
        this.Capabilities = Array.AsReadOnly(capabilities.ToArray());
    }

    /// <summary>Gets the bundle name.</summary>
    public string Name { get; }

    /// <summary>Gets the tools and skills selected for the turn.</summary>
    public IReadOnlyList<CapabilityEntry> Capabilities { get; }
}
