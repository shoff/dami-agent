using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Events;
using Dami.Persistence.ToolStaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.ToolStaging;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresToolVerificationStoreTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 23, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresToolVerificationStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task RecordAsync_Should_Atomically_Persist_Exact_Verification_And_Event_Async()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            new string('a', 64), "1 proposal test passed", at);
        IToolVerificationStore store = this.CreateVerificationStore();

        ToolVerificationRecord accepted = await store.RecordAsync(
            verification, CancellationToken.None);

        ToolVerificationRecord? found = await store.FindAsync(
            proposal.Request.ProposalId, proposal.ArtifactVersion, CancellationToken.None);
        ExecutionEvent[] events = (await this.ReplayAsync(proposal.Request.TraceId)).ToArray();
        Assert.Equal(verification, accepted);
        Assert.Equal(verification, found);
        Assert.Contains(events, item =>
            item.Type == ExecutionEventType.ToolVerified
            && item.EventId == verification.VerificationId
            && item.ParentSpanId == proposal.Request.SpanId);
    }

    [Fact]
    public async Task RecordAsync_Should_Normalize_VerifiedAt_To_Postgres_Precision_Async()
    {
        await this.fixture.ResetAsync();
        StagedToolProposal proposal = CreateProposal();
        await this.CreateProposalStore().StageAsync(proposal, CancellationToken.None);
        DateTimeOffset subMicrosecond = at.AddTicks(7);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            new string('a', 64), "1 proposal test passed", subMicrosecond);
        IToolVerificationStore store = this.CreateVerificationStore();

        ToolVerificationRecord accepted = await store.RecordAsync(
            verification, CancellationToken.None);

        Assert.Equal(at, accepted.VerifiedAt);
        Assert.Equal(
            accepted,
            await store.FindAsync(
                proposal.Request.ProposalId,
                proposal.ArtifactVersion,
                CancellationToken.None));
    }

    private PostgresToolVerificationStore CreateVerificationStore()
    {
        return new PostgresToolVerificationStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private PostgresToolProposalStore CreateProposalStore()
    {
        return new PostgresToolProposalStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
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
            Guid.NewGuid(), "verified-tool", "Verify an artifact.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["verification"],
            new Dictionary<string, string> { ["VerifiedTool.cs"] = "source" },
            new Dictionary<string, string> { ["VerifiedToolTests.cs"] = "tests" },
            "Repeated defects justify automation.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, at);
    }
}
