using System.Collections.Concurrent;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Thread-safe source-neutral registry of typed tool schemas.</summary>
public sealed class CapabilityToolSchemaRegistry :
    ICapabilityToolSchemaCatalog,
    ICapabilityToolSchemaRegistrar
{
    private readonly ConcurrentDictionary<Guid, CapabilityToolSchema> schemas = [];

    /// <inheritdoc />
    public void Register(CapabilityToolSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!this.schemas.TryAdd(schema.CapabilityId, schema))
        {
            throw new InvalidOperationException(
                $"A tool schema is already registered for capability '{schema.CapabilityId}'.");
        }
    }

    /// <inheritdoc />
    public CapabilityToolSchema? Find(Guid capabilityId)
    {
        return this.schemas.TryGetValue(capabilityId, out var schema) ? schema : null;
    }
}
