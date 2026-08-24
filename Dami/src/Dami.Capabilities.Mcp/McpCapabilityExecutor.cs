using Dami.Contracts.Capabilities;
using Dami.Contracts.Privacy;

namespace Dami.Capabilities.Mcp;

/// <summary>Dispatches source-neutral capability requests to registered MCP tools.</summary>
public sealed class McpCapabilityExecutor : ICapabilityExecutionSource
{
    private const int MAX_OUTPUT_CHARACTERS = 1_048_576;

    private readonly IMcpCapabilityCatalog catalog;
    private readonly int maxOutputCharacters;

    /// <summary>Creates the MCP capability executor.</summary>
    public McpCapabilityExecutor(
        IMcpCapabilityCatalog catalog,
        McpCapabilityExecutorOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxOutputCharacters is < 1 or > MAX_OUTPUT_CHARACTERS)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxOutputCharacters,
                $"MCP output limit must be between 1 and {MAX_OUTPUT_CHARACTERS} characters.");
        }

        this.catalog = catalog;
        this.maxOutputCharacters = options.MaxOutputCharacters;
    }

    /// <inheritdoc />
    public bool Owns(Guid capabilityId) => this.catalog.Find(capabilityId) is not null;

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CapabilityInvocation invocation = request.Invocation;
        McpCapabilityRegistration registration = this.catalog.Find(invocation.CapabilityId)
            ?? throw new KeyNotFoundException(
                $"MCP capability '{invocation.CapabilityId}' is not registered.");
        EgressOperationContext context = CreateContext(request);
        McpToolInvocationResult result = await registration.Invoker.InvokeAsync(
            registration.ToolName, invocation.Arguments, context, cancellationToken).ConfigureAwait(false);
        if (result.Output.Length > this.maxOutputCharacters)
        {
            throw new InvalidDataException(
                $"MCP capability '{invocation.CapabilityId}' output exceeded {this.maxOutputCharacters} characters.");
        }

        if (result.IsError)
        {
            throw new McpToolExecutionException(invocation.CapabilityId, result.Output);
        }

        return new CapabilityExecutionResult(
            result.Output,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "mcp",
                ["capability_id"] = invocation.CapabilityId.ToString("D"),
            });
    }

    private static EgressOperationContext CreateContext(CapabilityExecutionRequest request)
    {
        return new EgressOperationContext(
            "invoke MCP capability",
            request.Privacy,
            request.TraceId,
            request.SpanId,
            request.Origin);
    }
}
