using System.Collections.Concurrent;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Thread-safe ownership of sandboxed execution registrations.</summary>
public sealed class SandboxedCapabilityRegistry :
    ISandboxedCapabilityCatalog,
    IRevertibleRegistrar<SandboxedCapabilityRegistration>
{
    private readonly ConcurrentDictionary<Guid, SandboxedCapabilityRegistration> registrations = [];
    private readonly object writeGate = new();

    /// <inheritdoc />
    public void Register(SandboxedCapabilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (this.writeGate)
        {
            if (!this.registrations.TryAdd(registration.CapabilityId, registration))
            {
                throw new InvalidOperationException(
                    $"A sandboxed handler is already registered for capability '{registration.CapabilityId}'.");
            }
        }
    }

    /// <inheritdoc />
    public bool TryRemoveExact(SandboxedCapabilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (this.writeGate)
        {
            if (!this.registrations.TryGetValue(
                registration.CapabilityId, out SandboxedCapabilityRegistration? registered)
                || !ReferenceEquals(registered, registration))
            {
                return false;
            }

            return this.registrations.TryRemove(registration.CapabilityId, out _);
        }
    }

    /// <inheritdoc />
    public SandboxedCapabilityRegistration? Find(Guid capabilityId)
    {
        return this.registrations.TryGetValue(
            capabilityId, out SandboxedCapabilityRegistration? registration)
            ? registration
            : null;
    }
}
