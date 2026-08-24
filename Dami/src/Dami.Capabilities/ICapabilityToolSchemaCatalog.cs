using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Looks up typed model-facing schemas by stable capability identity.</summary>
public interface ICapabilityToolSchemaCatalog
{
    /// <summary>Returns the registered schema, or null when none exists.</summary>
    CapabilityToolSchema? Find(Guid capabilityId);
}
