using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Registers typed model-facing schemas during capability discovery.</summary>
public interface ICapabilityToolSchemaRegistrar
{
    /// <summary>Registers one immutable schema under its stable capability identity.</summary>
    void Register(CapabilityToolSchema schema);
}
