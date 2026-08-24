using System.Collections.Concurrent;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Thread-safe source-neutral registry of typed tool schemas.</summary>
public sealed class CapabilityToolSchemaRegistry :
    ICapabilityToolSchemaCatalog,
    ICapabilityToolSchemaRegistrar,
    IRevertibleRegistrar<CapabilityToolSchema>
{
    private readonly ConcurrentDictionary<Guid, CapabilityToolSchema> schemas = [];
    private readonly object writeGate = new();

    /// <inheritdoc />
    public void Register(CapabilityToolSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (this.writeGate)
        {
            if (!this.schemas.TryAdd(schema.CapabilityId, schema))
            {
                throw new InvalidOperationException(
                    $"A tool schema is already registered for capability '{schema.CapabilityId}'.");
            }
        }
    }

    /// <inheritdoc />
    public CapabilityToolSchema? Find(Guid capabilityId)
    {
        return this.schemas.TryGetValue(capabilityId, out var schema) ? schema : null;
    }

    /// <inheritdoc />
    public bool TryRemoveExact(CapabilityToolSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (this.writeGate)
        {
            if (!this.schemas.TryGetValue(
                schema.CapabilityId, out CapabilityToolSchema? registered)
                || !ReferenceEquals(registered, schema))
            {
                return false;
            }

            return this.schemas.TryRemove(schema.CapabilityId, out _);
        }
    }
}
