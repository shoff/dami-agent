using System.Collections.Concurrent;

namespace Dami.Capabilities.Mcp;

/// <summary>Thread-safe registry of stable MCP invocation targets.</summary>
public sealed class McpCapabilityRegistry : IMcpCapabilityCatalog, IMcpCapabilityRegistrar
{
    private readonly ConcurrentDictionary<Guid, McpCapabilityRegistration> registrations = [];

    /// <summary>Registers one stable capability mapping exactly once.</summary>
    public void Register(
        Guid capabilityId,
        Guid serverId,
        string toolName,
        IMcpToolInvoker invoker)
    {
        this.Register(new McpCapabilityRegistration(capabilityId, serverId, toolName, invoker));
    }

    /// <inheritdoc />
    public void Register(McpCapabilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!this.registrations.TryAdd(registration.CapabilityId, registration))
        {
            throw new InvalidOperationException(
                $"An MCP invocation is already registered for capability '{registration.CapabilityId}'.");
        }
    }

    /// <inheritdoc />
    public McpCapabilityRegistration? Find(Guid capabilityId)
    {
        return this.registrations.TryGetValue(
            capabilityId, out McpCapabilityRegistration? registration)
            ? registration
            : null;
    }
}
