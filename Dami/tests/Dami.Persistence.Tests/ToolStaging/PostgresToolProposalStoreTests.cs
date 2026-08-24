using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Events;
using Dami.Persistence.ToolStaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Dami.Persistence.Tests.ToolStaging;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresToolProposalStoreTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 16, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresToolProposalStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task StageAsync_Should_Atomically_Persist_The_Artifact_And_Proposed_Event()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        IToolProposalStore store = this.CreateStore();

        await store.StageAsync(proposal, CancellationToken.None);

        StagedToolProposal? found = await store.FindAsync(
            proposal.Request.ProposalId, CancellationToken.None);
        ExecutionEvent stagedEvent = (await this.ReplayAsync(proposal.Request.TraceId)).Single();
        Assert.Equal(
            (proposal.ArtifactVersion, "source", "tests", ExecutionEventType.ToolProposed,
                $"tool-proposal://{proposal.Request.ProposalId:D}"),
            (found?.ArtifactVersion,
                found?.Request.Artifact.SourceFiles["ReviewTool.cs"],
                found?.Request.Artifact.TestFiles["ReviewToolTests.cs"],
                stagedEvent.Type, stagedEvent.PayloadReference));
    }

    [Fact]
    public async Task StageAsync_Should_Store_The_Execution_Profile_By_Stable_Name()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateStore().StageAsync(proposal, CancellationToken.None);
        await using var command = this.fixture.DataSource.CreateCommand($"""
            select artifact ->> 'ExecutionProfile'
              from {DatabaseFixture.SCHEMA}.tool_proposals
             where proposal_id = @proposal;
            """);
        command.Parameters.AddWithValue("proposal", proposal.Request.ProposalId);

        Assert.Equal(
            "ReadOnly",
            await command.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StageAsync_Should_Reject_Conflicting_Trace_Provenance()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal first = CreateProposal();
        IToolProposalStore store = this.CreateStore();
        await store.StageAsync(first, CancellationToken.None);
        ToolProposalRequest source = first.Request;
        var conflictRequest = new ToolProposalRequest(
            source.ProposalId, Guid.NewGuid(), source.SpanId, source.ParentSpanId,
            source.Origin, source.Artifact);
        var conflict = new StagedToolProposal(
            conflictRequest, source.Artifact.Version, first.ProposedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StageAsync(conflict, CancellationToken.None));
    }

    [Fact]
    public async Task StageAsync_Should_Roll_Back_When_The_Event_Write_Fails()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        IToolProposalStore store = this.CreateStore();
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA, ExecutionEventType.ToolProposed);

        Exception? exception = await Record.ExceptionAsync(
            () => store.StageAsync(proposal, CancellationToken.None));
        StagedToolProposal? found = await store.FindAsync(
            proposal.Request.ProposalId, CancellationToken.None);

        Assert.Equal((typeof(PostgresException), true), (exception?.GetType(), found is null));
    }

    [Fact]
    public async Task StageAsync_Should_Roll_Back_When_The_Event_Id_Is_Already_Different()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        var collision = new ExecutionEvent(
            proposal.Request.ProposalId, Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, "test", ExecutionEventType.AgentProgressed,
            ExecutionStatus.Succeeded, at, "unrelated event", null, null);
        await this.CreateEventStore().AppendAsync(collision, CancellationToken.None);
        IToolProposalStore store = this.CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StageAsync(proposal, CancellationToken.None));

        Assert.Null(await store.FindAsync(proposal.Request.ProposalId, CancellationToken.None));
    }

    [Fact]
    public async Task StageAsync_Should_Converge_An_Exact_Retry_To_One_Event()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        IToolProposalStore store = this.CreateStore();

        await store.StageAsync(proposal, CancellationToken.None);
        var retry = new StagedToolProposal(
            proposal.Request, proposal.ArtifactVersion, proposal.ProposedAt.AddMinutes(1));
        StagedToolProposal accepted = await store.StageAsync(retry, CancellationToken.None);

        Assert.Equal(
            (proposal.ProposedAt, 1),
            (accepted.ProposedAt, (await this.ReplayAsync(proposal.Request.TraceId)).Count));
    }

    [Fact]
    public async Task StageAsync_Should_Persist_Contract_Valid_Json_Escaped_Source_And_Tests()
    {
        await this.fixture.ResetAsync();
        ToolProposalArtifact artifact = CreateArtifact(
            Guid.NewGuid(), new string('"', 600_000), new string('"', 600_000));
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.SelfAudit, artifact);
        var proposal = new StagedToolProposal(request, artifact.Version, at);

        StagedToolProposal accepted = await this.CreateStore()
            .StageAsync(proposal, CancellationToken.None);

        Assert.Equal(proposal.ArtifactVersion, accepted.ArtifactVersion);
    }

    [Fact]
    public async Task ListAsync_Should_Return_Bounded_Newest_First_Metadata_Without_Artifacts()
    {
        await this.fixture.ResetAsync();
        IToolProposalStore store = this.CreateStore();
        StagedToolProposal older = CreateProposal(at, "older-tool");
        StagedToolProposal newer = CreateProposal(at.AddMinutes(1), "newer-tool");
        await store.StageAsync(older, CancellationToken.None);
        await store.StageAsync(newer, CancellationToken.None);

        ToolProposalSummary summary = Assert.Single(
            await store.ListAsync(1, CancellationToken.None));

        Assert.Equal(
            (newer.Request.ProposalId, newer.Request.Artifact.Schema.CapabilityId,
                "newer-tool", newer.ArtifactVersion, ToolExecutionProfile.ReadOnly,
                newer.Request.Origin, newer.ProposedAt),
            (summary.ProposalId, summary.CapabilityId, summary.Name,
                summary.ArtifactVersion, summary.ExecutionProfile,
                summary.Origin, summary.ProposedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListAsync_Should_Enforce_Review_Page_Bounds(int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => this.CreateStore().ListAsync(limit, CancellationToken.None));
    }

    [Fact]
    public async Task Migration_Should_Make_Staged_Artifacts_Append_Only()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        IToolProposalStore store = this.CreateStore();
        await store.StageAsync(proposal, CancellationToken.None);
        await using var command = this.fixture.DataSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.tool_proposals set origin = 'UserTurn' "
            + "where proposal_id = @proposal;");
        command.Parameters.AddWithValue("proposal", proposal.Request.ProposalId);

        Exception? exception = await Record.ExceptionAsync(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.IsType<PostgresException>(exception);
    }

    [Fact]
    public async Task Migration_Should_Grant_The_Runtime_Only_Select_And_Insert()
    {
        await using var command = this.fixture.DataSource.CreateCommand($"""
            select has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_proposals', 'select')
               and has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_proposals', 'insert')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_proposals', 'update')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_proposals', 'delete')
               and not has_table_privilege(
                       'dami_app', '{DatabaseFixture.SCHEMA}.tool_proposals', 'truncate');
            """);

        Assert.True((bool)(await command.ExecuteScalarAsync(CancellationToken.None))!);
    }

    [Fact]
    public async Task Migration_Should_Reject_A_Capability_Id_That_Disagrees_With_The_Artifact()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await using var command = this.fixture.DataSource.CreateCommand($"""
            insert into {DatabaseFixture.SCHEMA}.tool_proposals
                (proposal_id, trace_id, span_id, parent_span_id, origin,
                 capability_id, artifact_version, artifact, proposed_at)
            values
                (@proposal, @trace, @span, null, 'SelfAudit',
                 @capability, @version, @artifact, @at);
            """);
        command.Parameters.AddWithValue("proposal", proposal.Request.ProposalId);
        command.Parameters.AddWithValue("trace", proposal.Request.TraceId);
        command.Parameters.AddWithValue("span", proposal.Request.SpanId);
        command.Parameters.AddWithValue("capability", Guid.NewGuid());
        command.Parameters.AddWithValue("version", proposal.ArtifactVersion);
        command.Parameters.Add(new NpgsqlParameter("artifact", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(proposal.Request.Artifact),
        });
        command.Parameters.AddWithValue("at", proposal.ProposedAt);

        await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Migration_Should_Reject_A_Version_That_Disagrees_With_The_Artifact()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await using var command = this.fixture.DataSource.CreateCommand($"""
            insert into {DatabaseFixture.SCHEMA}.tool_proposals
                (proposal_id, trace_id, span_id, parent_span_id, origin,
                 capability_id, artifact_version, artifact, proposed_at)
            values
                (@proposal, @trace, @span, null, 'SelfAudit',
                 @capability, @version, @artifact, @at);
            """);
        command.Parameters.AddWithValue("proposal", proposal.Request.ProposalId);
        command.Parameters.AddWithValue("trace", proposal.Request.TraceId);
        command.Parameters.AddWithValue("span", proposal.Request.SpanId);
        command.Parameters.AddWithValue(
            "capability", proposal.Request.Artifact.Schema.CapabilityId);
        command.Parameters.AddWithValue("version", new string('f', 64));
        command.Parameters.Add(new NpgsqlParameter("artifact", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(proposal.Request.Artifact),
        });
        command.Parameters.AddWithValue("at", proposal.ProposedAt);

        await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    private PostgresToolProposalStore CreateStore()
    {
        return new PostgresToolProposalStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private PostgresExecutionEventStore CreateEventStore()
    {
        return new PostgresExecutionEventStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresExecutionEventStore>.Instance);
    }

    private async Task<IReadOnlyList<ExecutionEvent>> ReplayAsync(Guid traceId)
    {
        var events = new List<ExecutionEvent>();
        await foreach (ExecutionEvent executionEvent in this.CreateEventStore()
            .ReplayAsync(traceId, CancellationToken.None))
        {
            events.Add(executionEvent);
        }

        return events;
    }

    private static StagedToolProposal CreateProposal(
        DateTimeOffset? proposedAt = null,
        string name = "review-tool")
    {
        var artifact = CreateArtifact(Guid.NewGuid(), "source", name: name);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ExecutionOrigin.SelfAudit, artifact);
        return new StagedToolProposal(request, artifact.Version, proposedAt ?? at);
    }

    private static ToolProposalArtifact CreateArtifact(
        Guid capabilityId,
        string source,
        string tests = "tests",
        string name = "review-tool")
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            capabilityId, name, "Review a bounded artifact.", parameters.RootElement);
        return new ToolProposalArtifact(
            schema, ["review"],
            new Dictionary<string, string> { ["ReviewTool.cs"] = source },
            new Dictionary<string, string> { ["ReviewToolTests.cs"] = tests },
            "An observation showed a repeated defect.", [Guid.NewGuid()],
            ToolExecutionProfile.ReadOnly);
    }
}
