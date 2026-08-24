using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native.Tests;

public sealed class NativeCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Dispatch_A_Snapshotted_Invocation_By_Stable_Id()
    {
        var capabilityId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var registry = new NativeCapabilityRegistry();
        registry.Register(capabilityId, handler);
        var executor = new NativeCapabilityExecutor(
            registry,
            new NativeCapabilityExecutorOptions { ExecutionTimeout = TimeSpan.FromSeconds(1) });
        var request = CreateRequest(capabilityId);

        CapabilityExecutionResult result = await executor
            .ExecuteAsync(request, CancellationToken.None);

        Assert.Same(request, handler.Request);
        Assert.Equal("notes.txt", handler.Request.Invocation.Arguments.GetProperty("path").GetString());
        Assert.Equal("completed", result.Output);
        Assert.Equal("notes.txt", result.Evidence["path"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Time_Out_A_Handler_That_Ignores_Cancellation()
    {
        var capabilityId = Guid.NewGuid();
        var registry = new NativeCapabilityRegistry();
        registry.Register(capabilityId, new IgnoringCancellationHandler());
        var executor = new NativeCapabilityExecutor(
            registry,
            new NativeCapabilityExecutorOptions { ExecutionTimeout = TimeSpan.FromMilliseconds(20) });
        var request = CreateRequest(capabilityId);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => executor.ExecuteAsync(request, CancellationToken.None));

        Assert.Contains(capabilityId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static CapabilityExecutionRequest CreateRequest(Guid capabilityId)
    {
        using var document = JsonDocument.Parse("{\"path\":\"notes.txt\"}");
        return TestCapabilityRequests.Create(capabilityId, document.RootElement);
    }

    private sealed class RecordingHandler : INativeCapabilityHandler
    {
        public CapabilityExecutionRequest Request { get; private set; } = null!;

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken)
        {
            this.Request = request;
            var result = new CapabilityExecutionResult(
                "completed",
                new Dictionary<string, string>
                {
                    ["path"] = request.Invocation.Arguments.GetProperty("path").GetString()!,
                });
            return Task.FromResult(result);
        }
    }

    private sealed class IgnoringCancellationHandler : INativeCapabilityHandler
    {
        public async Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            return new CapabilityExecutionResult(
                "late success",
                new Dictionary<string, string> { ["completed"] = "true" });
        }
    }
}
