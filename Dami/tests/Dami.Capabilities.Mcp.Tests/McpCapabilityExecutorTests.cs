using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Dispatch_Stable_Id_And_Translate_Success()
    {
        var capabilityId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var invoker = new RecordingInvoker(
            new McpToolInvocationResult("sunny in Austin", isError: false));
        var registry = new McpCapabilityRegistry();
        registry.Register(capabilityId, serverId, "weather", invoker);
        var executor = CreateExecutor(registry);
        CapabilityExecutionRequest request = CreateRequest(capabilityId, "Austin");

        CapabilityExecutionResult result = await executor.ExecuteAsync(
            request, CancellationToken.None);

        Assert.Equal("sunny in Austin", result.Output);
        Assert.Equal("mcp", result.Evidence["source"]);
        Assert.Equal(capabilityId.ToString("D"), result.Evidence["capability_id"]);
        Assert.Equal("weather", invoker.ToolName);
        Assert.Equal("Austin", invoker.Arguments.GetProperty("city").GetString());
        Assert.NotNull(invoker.Context);
        Assert.Equal(request.TraceId, invoker.Context.TraceId);
        Assert.Equal(request.SpanId, invoker.Context.ParentSpanId);
        Assert.Equal(PrivacyClass.Egressable, invoker.Context.Privacy);
        Assert.Equal(ExecutionOrigin.SelfAudit, invoker.Context.Origin);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Translate_A_Tool_Error_Without_Losing_Identity()
    {
        var capabilityId = Guid.NewGuid();
        var invoker = new RecordingInvoker(
            new McpToolInvocationResult("city is required", isError: true));
        var registry = new McpCapabilityRegistry();
        registry.Register(capabilityId, Guid.NewGuid(), "weather", invoker);
        var executor = CreateExecutor(registry);

        McpToolExecutionException exception = await Assert.ThrowsAsync<McpToolExecutionException>(
            () => executor.ExecuteAsync(
                CreateRequest(capabilityId, string.Empty), CancellationToken.None));

        Assert.Equal(capabilityId, exception.CapabilityId);
        Assert.Equal("city is required", exception.RemoteMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Propagate_Caller_Cancellation_To_The_Invoker()
    {
        var capabilityId = Guid.NewGuid();
        var invoker = new CancellableInvoker();
        var registry = new McpCapabilityRegistry();
        registry.Register(capabilityId, Guid.NewGuid(), "wait", invoker);
        var executor = CreateExecutor(registry);
        using var cancellation = new CancellationTokenSource();

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                CreateRequest(capabilityId, "Austin"), cancellation.Token));
        Assert.Equal(cancellation.Token, invoker.ObservedToken);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Output_Above_The_Configured_Bound()
    {
        var capabilityId = Guid.NewGuid();
        var registry = new McpCapabilityRegistry();
        registry.Register(
            capabilityId,
            Guid.NewGuid(),
            "verbose",
            new RecordingInvoker(new McpToolInvocationResult("12345", isError: false)));
        var executor = new McpCapabilityExecutor(
            registry, new McpCapabilityExecutorOptions { MaxOutputCharacters = 4 });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => executor.ExecuteAsync(
                CreateRequest(capabilityId, "Austin"), CancellationToken.None));

        Assert.Contains("exceeded 4 characters", exception.Message, StringComparison.Ordinal);
    }

    private static CapabilityExecutionRequest CreateRequest(Guid capabilityId, string city)
    {
        using var document = JsonDocument.Parse($$"""{"city":"{{city}}"}""");
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.Egressable, ExecutionOrigin.SelfAudit,
            new CapabilityInvocation(capabilityId, document.RootElement));
    }

    private static McpCapabilityExecutor CreateExecutor(IMcpCapabilityCatalog catalog)
    {
        return new McpCapabilityExecutor(catalog, new McpCapabilityExecutorOptions());
    }

    private sealed class RecordingInvoker(McpToolInvocationResult result) : IMcpToolInvoker
    {
        public string? ToolName { get; private set; }

        public JsonElement Arguments { get; private set; }

        public EgressOperationContext? Context { get; private set; }

        public Task<McpToolInvocationResult> InvokeAsync(
            string toolName,
            JsonElement arguments,
            EgressOperationContext context,
            CancellationToken cancellationToken)
        {
            this.ToolName = toolName;
            this.Arguments = arguments.Clone();
            this.Context = context;
            return Task.FromResult(result);
        }
    }

    private sealed class CancellableInvoker : IMcpToolInvoker
    {
        public CancellationToken ObservedToken { get; private set; }

        public async Task<McpToolInvocationResult> InvokeAsync(
            string toolName,
            JsonElement arguments,
            EgressOperationContext context,
            CancellationToken cancellationToken)
        {
            this.ObservedToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new McpToolInvocationResult("unreachable", isError: false);
        }
    }
}
