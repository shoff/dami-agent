using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedToolActivatorTests
{
    [Fact]
    public async Task ActivateAsync_Should_Publish_Exact_Registration_Only_Once_Async()
    {
        ToolActivationRecoveryItem item = CreateItem();
        var handlers = new SandboxedCapabilityRegistry();
        var schemas = new CapabilityToolSchemaRegistry();
        var capabilities = new CapabilityRegistry();
        var publisher = new SandboxedCapabilityPublisher(handlers, schemas, capabilities);
        var materializer = new Materializer(item);
        var activator = new SandboxedToolActivator(
            materializer, publisher, handlers, schemas, capabilities,
            new StubTimeProvider(DateTimeOffset.UnixEpoch));

        await activator.ActivateAsync(item, CancellationToken.None);
        await activator.ActivateAsync(item, CancellationToken.None);

        Guid capabilityId = item.Proposal.Request.Artifact.Schema.CapabilityId;
        Assert.Same(materializer.Registration, handlers.Find(capabilityId));
        Assert.Same(item.Proposal.Request.Artifact.Schema, schemas.Find(capabilityId));
        CapabilityEntry entry = Assert.IsType<CapabilityEntry>(capabilities.Find(capabilityId));
        Assert.Equal((CapabilitySource.Sandboxed, item.Proposal.ArtifactVersion),
            (entry.Source, entry.Version));
        Assert.Equal(2, materializer.CallCount);
    }

    private static ToolActivationRecoveryItem CreateItem()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "recoverable", "Recover this tool.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["recovery"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "Recovery must converge.", [Guid.NewGuid()], ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        var proposal = new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), request.ProposalId, artifact.Version,
            new string('a', 64), "tests_passed=1", DateTimeOffset.UnixEpoch);
        return new ToolActivationRecoveryItem(Guid.NewGuid(), proposal, verification, false);
    }

    private sealed class Materializer(ToolActivationRecoveryItem item)
        : ISandboxedToolMaterializer
    {
        public int CallCount { get; private set; }

        public SandboxedCapabilityRegistration Registration { get; } = new(
            item.Proposal.Request.Artifact.Schema.CapabilityId,
            item.Verification,
            Path.Combine(Path.GetTempPath(), item.PromotionId.ToString("N")));

        public Task<SandboxedCapabilityRegistration> MaterializeAsync(
            Guid promotionId,
            StagedToolProposal proposal,
            ToolVerificationRecord verification,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(this.Registration);
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
