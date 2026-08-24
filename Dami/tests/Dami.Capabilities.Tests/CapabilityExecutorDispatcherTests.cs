using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Tests;

public sealed class CapabilityExecutorDispatcherTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Invoke_The_Only_Source_Owning_The_Stable_Id()
    {
        var capabilityId = Guid.NewGuid();
        var skipped = new StubSource(Guid.NewGuid(), "unused");
        var selected = new StubSource(capabilityId, "remote result");
        var dispatcher = new CapabilityExecutorDispatcher([skipped, selected]);
        CapabilityExecutionRequest request = CreateRequest(capabilityId);

        CapabilityExecutionResult result = await dispatcher.ExecuteAsync(
            request, CancellationToken.None);

        Assert.Equal("remote result", result.Output);
        Assert.Equal(0, skipped.CallCount);
        Assert.Equal(1, selected.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Ambiguous_Source_Ownership()
    {
        var capabilityId = Guid.NewGuid();
        var dispatcher = new CapabilityExecutorDispatcher(
            [new StubSource(capabilityId, "one"), new StubSource(capabilityId, "two")]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.ExecuteAsync(CreateRequest(capabilityId), CancellationToken.None));

        Assert.Contains(capabilityId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_An_Unowned_Capability()
    {
        var capabilityId = Guid.NewGuid();
        var dispatcher = new CapabilityExecutorDispatcher([]);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => dispatcher.ExecuteAsync(CreateRequest(capabilityId), CancellationToken.None));

        Assert.Contains(capabilityId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static CapabilityExecutionRequest CreateRequest(Guid capabilityId)
    {
        using var document = JsonDocument.Parse("{}");
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, document.RootElement));
    }

    private sealed class StubSource(Guid ownedId, string output) : ICapabilityExecutionSource
    {
        public int CallCount { get; private set; }

        public bool Owns(Guid capabilityId) => capabilityId == ownedId;

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(new CapabilityExecutionResult(
                output, new Dictionary<string, string> { ["test"] = "true" }));
        }
    }
}
