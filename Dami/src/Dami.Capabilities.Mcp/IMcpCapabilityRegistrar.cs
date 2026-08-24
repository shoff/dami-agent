namespace Dami.Capabilities.Mcp;

/// <summary>Publishes executable MCP registrations.</summary>
public interface IMcpCapabilityRegistrar
{
    /// <summary>Registers one stable capability mapping exactly once.</summary>
    void Register(McpCapabilityRegistration registration);
}
