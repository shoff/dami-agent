using System.Collections.Concurrent;

namespace Dami.Capabilities.Native;

/// <summary>Thread-safe ownership of activated native capability handlers.</summary>
public sealed class NativeCapabilityRegistry : INativeCapabilityCatalog, INativeCapabilityRegistrar
{
    private readonly ConcurrentDictionary<Guid, INativeCapabilityHandler> handlers = [];

    /// <inheritdoc />
    public void Register(Guid capabilityId, INativeCapabilityHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException("A native handler requires a stable identifier.", nameof(capabilityId));
        }

        if (!this.handlers.TryAdd(capabilityId, handler))
        {
            throw new InvalidOperationException(
                $"A native handler is already registered for capability '{capabilityId}'.");
        }
    }

    /// <inheritdoc />
    public INativeCapabilityHandler? Find(Guid capabilityId)
    {
        return this.handlers.TryGetValue(capabilityId, out INativeCapabilityHandler? handler)
            ? handler
            : null;
    }
}
