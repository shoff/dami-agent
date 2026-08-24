using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Dami.Capabilities.Mcp;

internal sealed class McpToolSchemaCache
{
    private readonly Guid serverId;
    private IReadOnlyDictionary<string, JsonElement> schemas =
        new Dictionary<string, JsonElement>();

    public McpToolSchemaCache(Guid serverId)
    {
        this.serverId = serverId;
    }

    public IReadOnlyList<McpToolDescriptor> Replace(IList<McpClientTool> tools)
    {
        var replacement = new Dictionary<string, JsonElement>(tools.Count, StringComparer.Ordinal);
        var descriptors = new McpToolDescriptor[tools.Count];
        for (var index = 0; index < tools.Count; index++)
        {
            McpClientTool tool = tools[index];
            string reference = this.ReferenceFor(tool.Name);
            JsonElement schema = tool.ProtocolTool.InputSchema.Clone();
            replacement.Add(reference, schema);
            descriptors[index] = new McpToolDescriptor(
                tool.Name, tool.Description, reference, VersionFor(schema));
        }

        Volatile.Write(ref this.schemas, replacement);
        return Array.AsReadOnly(descriptors);
    }

    public JsonElement? Find(string schemaReference)
    {
        IReadOnlyDictionary<string, JsonElement> snapshot = Volatile.Read(ref this.schemas);
        return snapshot.TryGetValue(schemaReference, out JsonElement schema) ? schema : null;
    }

    private string ReferenceFor(string toolName)
    {
        return $"mcp://{this.serverId:D}/tools/{Uri.EscapeDataString(toolName)}/schema";
    }

    private static string VersionFor(JsonElement schema)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(schema);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }
}
