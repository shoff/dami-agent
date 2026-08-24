using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native.Tests;

public sealed class NativeCapabilityActivatorTests
{
    [Fact]
    public void Activate_Should_Register_The_Factory_Result_Under_Discovered_Identity()
    {
        var capabilityId = Guid.NewGuid();
        var entry = new CapabilityEntry(
            capabilityId, "test-tool", "Test tool.", CapabilityKind.Tool,
            CapabilitySource.Native, TrustLevel.Trusted, [], "native://test", null, [],
            "1.0.0", DateTimeOffset.UnixEpoch);
        var schema = new CapabilityToolSchema(
            capabilityId, entry.Name, entry.Description,
            JsonSerializer.SerializeToElement(new { type = "object" }));
        var registration = new NativeCapabilityRegistration(typeof(StubHandler), entry, schema);
        var handler = new StubHandler();
        var registry = new NativeCapabilityRegistry();
        var activator = new NativeCapabilityActivator(registry);

        activator.Activate([registration], type => type == typeof(StubHandler) ? handler : null);

        Assert.Same(handler, registry.Find(capabilityId));
    }

    private sealed class StubHandler : INativeCapabilityHandler
    {
        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
