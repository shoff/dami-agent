namespace Dami.Capabilities.Mcp;

/// <summary>Looks up executable MCP registrations without exposing mutation.</summary>
public interface IMcpCapabilityCatalog
{
    /// <summary>Finds one executable registration by stable capability identifier.</summary>
    McpCapabilityRegistration? Find(Guid capabilityId);
}
