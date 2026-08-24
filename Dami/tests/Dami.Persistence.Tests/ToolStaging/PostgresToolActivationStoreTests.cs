using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Approvals;
using Dami.Persistence.Events;
using Dami.Persistence.ToolStaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests.ToolStaging;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresToolActivationStoreTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 23, 30, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresToolActivationStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task RecordAsync_Should_Persist_A_Failed_Attempt_And_Event_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        var failure = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Failed, "InvalidOperationException", at.AddMinutes(3));
        IToolActivationStore store = this.CreateActivationStore();

        ToolActivationOutcome accepted = await store.RecordAsync(
            failure, CancellationToken.None);

        Assert.Equal(failure, accepted);
        Assert.Null(await store.FindActivatedAsync(promotion.PromotionId, CancellationToken.None));
        Assert.Contains(
            await this.ReplayAsync(proposal.Request.TraceId),
            item => item.Type == ExecutionEventType.ToolActivationFailed
                && item.EventId == failure.ActivationId
                && item.Status == ExecutionStatus.Failed);
    }

    [Fact]
    public async Task RecordAsync_Should_Roll_Back_Outcome_When_Its_Event_Fails_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        var activation = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Activated, null, at.AddMinutes(3));
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA, ExecutionEventType.ToolActivated);

        await Assert.ThrowsAsync<PostgresException>(() => this.CreateActivationStore()
            .RecordAsync(activation, CancellationToken.None));

        Assert.Null(await this.CreateActivationStore().FindActivatedAsync(
            promotion.PromotionId, CancellationToken.None));
        Assert.DoesNotContain(
            await this.ReplayAsync(proposal.Request.TraceId),
            item => item.EventId == activation.ActivationId);
    }

    [Fact]
    public async Task RecordAsync_Should_Reject_A_Failure_After_Activation_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        IToolActivationStore store = this.CreateActivationStore();
        var activated = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Activated, null, at.AddMinutes(3));
        await store.RecordAsync(activated, CancellationToken.None);
        var laterFailure = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Failed, "InvalidOperationException", at.AddMinutes(4));

        await Assert.ThrowsAsync<PostgresException>(
            () => store.RecordAsync(laterFailure, CancellationToken.None));

        Assert.Equal(activated, await store.FindActivatedAsync(
            promotion.PromotionId, CancellationToken.None));
        Assert.DoesNotContain(
            await this.ReplayAsync(proposal.Request.TraceId),
            item => item.EventId == laterFailure.ActivationId);
    }

    [Fact]
    public async Task RecordAsync_Should_Atomically_Persist_Activated_Outcome_And_Event_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        var activation = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Activated, null, at.AddMinutes(3));
        IToolActivationStore store = this.CreateActivationStore();

        ToolActivationOutcome accepted = await store.RecordAsync(
            activation, CancellationToken.None);

        ToolActivationOutcome? found = await store.FindActivatedAsync(
            promotion.PromotionId, CancellationToken.None);
        ExecutionEvent[] events = (await this.ReplayAsync(proposal.Request.TraceId)).ToArray();
        Assert.Equal(activation, accepted);
        Assert.Equal(activation, found);
        Assert.Contains(events, item =>
            item.Type == ExecutionEventType.ToolActivated
            && item.EventId == activation.ActivationId
            && item.ParentSpanId == promotion.PromotionId);
    }

    [Fact]
    public async Task FindAsync_Should_Return_Approved_Exact_Pending_Activation_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        var source = this.CreateRecoverySource();

        IReadOnlyList<ToolActivationRecoveryItem> items = await source.FindAsync(
            10, CancellationToken.None);

        ToolActivationRecoveryItem item = Assert.Single(items);
        Assert.Equal(promotion.PromotionId, item.PromotionId);
        Assert.Equal(proposal.Request.ProposalId, item.Proposal.Request.ProposalId);
        Assert.Equal(proposal.ArtifactVersion, item.Proposal.ArtifactVersion);
        Assert.Equal(
            proposal.Request.Artifact.Schema.CapabilityId,
            item.Proposal.Request.Artifact.Schema.CapabilityId);
        Assert.Equal(verification, item.Verification);
        Assert.False(item.IsActivated);
    }

    [Fact]
    public async Task FindAsync_Should_Return_Activated_Items_For_Startup_Republication_Async()
    {
        await this.fixture.ResetAsync();
        (StagedToolProposal proposal, ToolVerificationRecord verification) =
            await this.StageAndVerifyAsync();
        ToolPromotionRequest promotion = await this.PromoteAndApproveAsync(proposal);
        var activated = new ToolActivationOutcome(
            Guid.NewGuid(), promotion.PromotionId, verification.VerificationId,
            ToolActivationStatus.Activated, null, at.AddMinutes(3));
        await this.CreateActivationStore().RecordAsync(activated, CancellationToken.None);

        IReadOnlyList<ToolActivationRecoveryItem> items = await this.CreateRecoverySource()
            .FindAsync(10, CancellationToken.None);

        Assert.True(Assert.Single(items).IsActivated);
    }

    private PostgresToolActivationStore CreateActivationStore()
    {
        return new PostgresToolActivationStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private PostgresToolActivationRecoverySource CreateRecoverySource()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return new PostgresToolActivationRecoverySource(
            this.fixture.DataSource,
            options,
            new PostgresToolProposalStore(this.fixture.DataSource, options),
            new PostgresToolVerificationStore(this.fixture.DataSource, options));
    }

    private async Task<(StagedToolProposal Proposal, ToolVerificationRecord Verification)>
        StageAndVerifyAsync()
    {
        StagedToolProposal proposal = CreateProposal();
        var proposals = new PostgresToolProposalStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        await proposals.StageAsync(proposal, CancellationToken.None);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            new string('a', 64), "1 proposal test passed", at.AddMinutes(1));
        var verifications = new PostgresToolVerificationStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        await verifications.RecordAsync(verification, CancellationToken.None);
        return (proposal, verification);
    }

    private async Task<ToolPromotionRequest> PromoteAndApproveAsync(StagedToolProposal proposal)
    {
        var approval = new ApprovalRequest(
            Guid.NewGuid(), proposal.Request.TraceId, ToolPromotionRequest.REQUESTED_BY,
            "Promote the exact verified tool artifact.", ToolPromotionRequest.SCOPE,
            ToolPromotionRequest.Resource(proposal.Request.ProposalId, proposal.ArtifactVersion),
            at.AddMinutes(2), origin: proposal.Request.Origin,
            parentSpanId: proposal.Request.SpanId);
        var promotion = new ToolPromotionRequest(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion, approval);
        var promotions = new PostgresToolPromotionStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        await promotions.RequestAsync(promotion, CancellationToken.None);
        await this.CreateApprovalStore().ResolveAsync(
            approval.ApprovalId, ApprovalStatus.Approved, "approved after verification",
            at.AddMinutes(2).AddSeconds(1), CancellationToken.None);
        return promotion;
    }

    private PostgresApprovalService CreateApprovalStore()
    {
        return new PostgresApprovalService(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresApprovalService>.Instance);
    }

    private async Task<IReadOnlyList<ExecutionEvent>> ReplayAsync(Guid traceId)
    {
        var store = new PostgresExecutionEventStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresExecutionEventStore>.Instance);
        var events = new List<ExecutionEvent>();
        await foreach (ExecutionEvent item in store.ReplayAsync(traceId, CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "activated-tool", "Activate an artifact.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["activation"],
            new Dictionary<string, string> { ["ActivatedTool.cs"] = "source" },
            new Dictionary<string, string> { ["ActivatedToolTests.cs"] = "tests" },
            "Repeated defects justify automation.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, at);
    }
}
