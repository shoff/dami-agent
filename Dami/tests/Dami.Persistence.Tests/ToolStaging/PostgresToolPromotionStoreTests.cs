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
public sealed class PostgresToolPromotionStoreTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 22, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresToolPromotionStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task RequestAsync_Should_Atomically_Persist_Promotion_Approval_And_Events()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        ToolPromotionRequest promotion = CreatePromotion(proposal);
        IToolPromotionStore store = this.CreatePromotionStore();

        await store.RequestAsync(promotion, CancellationToken.None);

        ToolPromotionRequest? found = await store.FindByApprovalAsync(
            promotion.Approval.ApprovalId, CancellationToken.None);
        ApprovalRequest? approval = await this.CreateApprovalStore().FindAsync(
            promotion.Approval.ApprovalId, CancellationToken.None);
        ExecutionEventType[] types = (await this.ReplayAsync(proposal.Request.TraceId))
            .Select(item => item.Type).ToArray();
        Assert.Equal(
            (promotion.PromotionId, ApprovalStatus.Pending, true, true),
            (found?.PromotionId, approval?.Status,
                types.Contains(ExecutionEventType.ToolPromotionRequested),
                types.Contains(ExecutionEventType.ApprovalRequested)));
    }

    [Fact]
    public async Task Migration_Should_Reject_An_Approval_Outside_The_Promotion_Boundary()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), proposal.Request.TraceId, "another-component", "Promote tool.",
            "another-scope", ToolPromotionRequest.Resource(
                proposal.Request.ProposalId, proposal.ArtifactVersion),
            at, parentSpanId: proposal.Request.SpanId);
        await this.CreateApprovalStore().RequestAsync(approval, CancellationToken.None);
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand($"""
            insert into {DatabaseFixture.SCHEMA}.tool_promotions
                (promotion_id, approval_id, proposal_id, artifact_version)
            values (@promotion, @approval, @proposal, @version);
            """);
        command.Parameters.AddWithValue("promotion", Guid.NewGuid());
        command.Parameters.AddWithValue("approval", approval.ApprovalId);
        command.Parameters.AddWithValue("proposal", proposal.Request.ProposalId);
        command.Parameters.AddWithValue("version", proposal.ArtifactVersion);

        await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_Should_Converge_An_Exact_Retry_After_Resolution()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        ToolPromotionRequest promotion = CreatePromotion(proposal);
        IToolPromotionStore store = this.CreatePromotionStore();
        await store.RequestAsync(promotion, CancellationToken.None);
        await this.CreateApprovalStore().ResolveAsync(
            promotion.Approval.ApprovalId, ApprovalStatus.Approved, "verified",
            at.AddMinutes(1), CancellationToken.None);

        ToolPromotionRequest accepted = await store.RequestAsync(
            promotion, CancellationToken.None);

        Assert.Equal(promotion, accepted);
        Assert.Equal(4, (await this.ReplayAsync(proposal.Request.TraceId)).Count);
    }

    [Fact]
    public async Task RequestAsync_Should_Roll_Back_All_Rows_When_The_Promotion_Event_Fails()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        ToolPromotionRequest promotion = CreatePromotion(proposal);
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA,
            ExecutionEventType.ToolPromotionRequested);

        await Assert.ThrowsAsync<PostgresException>(() => this.CreatePromotionStore()
            .RequestAsync(promotion, CancellationToken.None));

        Assert.Null(await this.CreatePromotionStore().FindByApprovalAsync(
            promotion.Approval.ApprovalId, CancellationToken.None));
        Assert.Null(await this.CreateApprovalStore().FindAsync(
            promotion.Approval.ApprovalId, CancellationToken.None));
        Assert.Single(await this.ReplayAsync(proposal.Request.TraceId));
    }

    [Fact]
    public async Task RequestAsync_Should_Roll_Back_A_Conflicting_Approval()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal firstProposal = CreateProposal();
        StagedToolProposal secondProposal = CreateProposal();
        IToolProposalStore proposals = this.CreateProposalStore();
        await proposals.StageAsync(firstProposal, CancellationToken.None);
        await proposals.StageAsync(secondProposal, CancellationToken.None);
        ToolPromotionRequest first = CreatePromotion(firstProposal);
        ToolPromotionRequest conflicting = CreatePromotion(secondProposal, first.PromotionId);
        IToolPromotionStore promotions = this.CreatePromotionStore();
        await promotions.RequestAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => promotions.RequestAsync(conflicting, CancellationToken.None));

        Assert.Null(await this.CreateApprovalStore().FindAsync(
            conflicting.Approval.ApprovalId, CancellationToken.None));
        Assert.Equal(first, await promotions.FindByApprovalAsync(
            first.Approval.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task Migration_Should_Make_Promotions_Append_Only()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        ToolPromotionRequest promotion = CreatePromotion(proposal);
        await this.CreatePromotionStore().RequestAsync(promotion, CancellationToken.None);
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand(
            $"delete from {DatabaseFixture.SCHEMA}.tool_promotions "
            + "where promotion_id = @promotion;");
        command.Parameters.AddWithValue("promotion", promotion.PromotionId);

        await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Migration_Should_Grant_The_Runtime_Only_Select_And_Insert()
    {
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand($"""
            select has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'select')
               and has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'insert')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'update')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'delete')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'truncate')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'references')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_promotions', 'trigger');
            """);

        Assert.True((bool)(await command.ExecuteScalarAsync(CancellationToken.None))!);
    }

    private PostgresToolPromotionStore CreatePromotionStore()
    {
        return new PostgresToolPromotionStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private PostgresToolProposalStore CreateProposalStore()
    {
        return new PostgresToolProposalStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
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

    private static ToolPromotionRequest CreatePromotion(
        StagedToolProposal proposal,
        Guid? promotionId = null)
    {
        var approval = new ApprovalRequest(
            Guid.NewGuid(), proposal.Request.TraceId, ToolPromotionRequest.REQUESTED_BY,
            "Promote the exact verified tool artifact.", ToolPromotionRequest.SCOPE,
            ToolPromotionRequest.Resource(
                proposal.Request.ProposalId, proposal.ArtifactVersion),
            at, origin: proposal.Request.Origin, parentSpanId: proposal.Request.SpanId);
        return new ToolPromotionRequest(
            promotionId ?? Guid.NewGuid(), proposal.Request.ProposalId,
            proposal.ArtifactVersion, approval);
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "review-tool", "Review an artifact.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["review"],
            new Dictionary<string, string> { ["ReviewTool.cs"] = "source" },
            new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests" },
            "Repeated defects justify automation.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, at);
    }
}
