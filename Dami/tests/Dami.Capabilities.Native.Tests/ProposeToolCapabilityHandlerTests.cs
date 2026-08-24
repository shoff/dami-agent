using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Native.Tests;

public sealed class ProposeToolCapabilityHandlerTests
{
    private static readonly DateTimeOffset now =
        new(2026, 8, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_Should_Stage_A_Trace_Owned_Inert_Artifact()
    {
        var capabilityId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var toolSpanId = Guid.NewGuid();
        var store = new RecordingStore();
        JsonElement arguments = CreateArguments(capabilityId, observationId);
        var request = new CapabilityExecutionRequest(
            traceId, toolSpanId, PrivacyClass.LocalOnly, ExecutionOrigin.SelfAudit,
            new CapabilityInvocation(Guid.NewGuid(), arguments));

        CapabilityExecutionResult result = await new ProposeToolCapabilityHandler(
            store, new FixedTimeProvider(now)).ExecuteAsync(request, CancellationToken.None);

        AssertStaged(store.Proposal!, result, traceId, toolSpanId, capabilityId, observationId);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Derive_Retry_Stable_Proposal_And_Child_Span_Ids()
    {
        var store = new RecordingStore();
        var request = new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.SelfAudit,
            new CapabilityInvocation(
                Guid.NewGuid(), CreateArguments(Guid.NewGuid(), Guid.NewGuid())));
        var handler = new ProposeToolCapabilityHandler(store, new FixedTimeProvider(now));

        await handler.ExecuteAsync(request, CancellationToken.None);
        await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(
            (store.Proposals[0].Request.ProposalId, store.Proposals[0].Request.SpanId),
            (store.Proposals[1].Request.ProposalId, store.Proposals[1].Request.SpanId));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_The_Canonical_Review_Identity_To_The_Model()
    {
        var store = new RecordingStore();
        var request = new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.SelfAudit,
            new CapabilityInvocation(
                Guid.NewGuid(), CreateArguments(Guid.NewGuid(), Guid.NewGuid())));

        CapabilityExecutionResult result = await new ProposeToolCapabilityHandler(
            store, new FixedTimeProvider(now)).ExecuteAsync(request, CancellationToken.None);

        Assert.Contains(store.Proposal!.Request.ProposalId.ToString("D"), result.Output);
        Assert.Contains(store.Proposal.ArtifactVersion, result.Output);
    }

    [Fact]
    public void Discovery_Should_Advertise_The_Inert_Proposal_Boundary()
    {
        NativeCapabilityRegistration registration = new NativeCapabilityDiscovery().Discover(
            typeof(ProposeToolCapabilityHandler).Assembly, now)
            .Single(item => item.ImplementationType == typeof(ProposeToolCapabilityHandler));

        Assert.Equal(
            ("propose-tool", CapabilitySource.Native, TrustLevel.Trusted,
                "native://propose-tool/schema/v1", "tools|authoring|staging|review"),
            (registration.Entry.Name, registration.Entry.Source, registration.Entry.Trust,
                registration.Entry.SchemaReference, string.Join('|', registration.Entry.Tags)));
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProposeToolCapabilityHandler(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(
            () => new ProposeToolCapabilityHandler(new RecordingStore(), null!));
    }

    private static void AssertStaged(
        StagedToolProposal staged,
        CapabilityExecutionResult result,
        Guid traceId,
        Guid toolSpanId,
        Guid capabilityId,
        Guid observationId)
    {
        Assert.Equal(
            (traceId, (Guid?)toolSpanId, ExecutionOrigin.SelfAudit, capabilityId,
                "source", "tests", observationId, ToolExecutionProfile.ReadOnly, now,
                "false", "false"),
            (staged.Request.TraceId, staged.Request.ParentSpanId, staged.Request.Origin,
                staged.Request.Artifact.Schema.CapabilityId,
                staged.Request.Artifact.SourceFiles["ReviewTool.cs"],
                staged.Request.Artifact.TestFiles["ReviewToolTests.cs"],
                staged.Request.Artifact.ObservationIds.Single(),
                staged.Request.Artifact.ExecutionProfile, staged.ProposedAt,
                result.Evidence["registered"], result.Evidence["executed"]));
        Assert.NotEqual(toolSpanId, staged.Request.SpanId);
    }

    private static JsonElement CreateArguments(Guid capabilityId, Guid observationId)
    {
        return JsonSerializer.SerializeToElement(new
        {
            capabilityId,
            name = "review-tool",
            description = "Review one bounded artifact.",
            parameters = new { type = "object" },
            tags = new[] { "review" },
            sourceFiles = new Dictionary<string, string> { ["ReviewTool.cs"] = "source" },
            testFiles = new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests" },
            rationale = "Repeated review failures justify automation.",
            observationIds = new[] { observationId },
            executionProfile = "ReadOnly",
        });
    }

    private sealed class RecordingStore : IToolProposalStore
    {
        public List<StagedToolProposal> Proposals { get; } = [];

        public StagedToolProposal? Proposal { get; private set; }

        public Task<StagedToolProposal> StageAsync(
            StagedToolProposal proposal,
            CancellationToken cancellationToken)
        {
            this.Proposal = proposal;
            this.Proposals.Add(proposal);
            return Task.FromResult(proposal);
        }

        public Task<StagedToolProposal?> FindAsync(
            Guid proposalId,
            CancellationToken cancellationToken) => Task.FromResult<StagedToolProposal?>(null);

        public Task<IReadOnlyList<ToolProposalSummary>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ToolProposalSummary>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
