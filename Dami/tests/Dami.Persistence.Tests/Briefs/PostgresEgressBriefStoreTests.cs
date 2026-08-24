using Dami.Contracts.Approvals;
using Dami.Contracts.Briefs;
using Dami.Persistence.Approvals;
using Dami.Persistence.Briefs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Briefs;

/// <summary>The consent artifact round-trips exactly, answer recorded next to what was sent.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresEgressBriefStoreTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 23, 21, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresEgressBriefStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task FindByApprovalAsync_Should_Return_The_Exact_Stored_Bytes()
    {
        await this.fixture.ResetAsync();
        var (store, brief) = await this.CreateStoredBriefAsync("the exact brief text");

        var found = await store.FindByApprovalAsync(brief.ApprovalId, CancellationToken.None);

        Assert.Equal("the exact brief text", found!.Brief);
    }

    [Fact]
    public async Task FindByApprovalAsync_Should_Return_Null_For_An_Unknown_Approval()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();

        Assert.Null(await store.FindByApprovalAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task MarkSentAsync_Should_Record_The_Answer_And_When()
    {
        await this.fixture.ResetAsync();
        var (store, brief) = await this.CreateStoredBriefAsync("outbound");

        await store.MarkSentAsync(brief.BriefId, "the frontier's answer", at.AddMinutes(5), CancellationToken.None);

        var found = await store.FindByApprovalAsync(brief.ApprovalId, CancellationToken.None);
        Assert.Equal("the frontier's answer", found!.Answer);
    }

    private async Task<(PostgresEgressBriefStore, EgressBrief)> CreateStoredBriefAsync(string text)
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        var approvals = new PostgresApprovalService(
            this.fixture.DataSource, options, NullLogger<PostgresApprovalService>.Instance);
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "frontier-brief", "send brief", "egress", "codex", at);
        await approvals.RequestAsync(approval, CancellationToken.None);

        var store = this.CreateStore();
        var brief = new EgressBrief(
            Guid.NewGuid(), approval.ApprovalId, approval.TraceId, "a question", text, "hash", at);
        await store.CreateAsync(brief, CancellationToken.None);
        return (store, brief);
    }

    private PostgresEgressBriefStore CreateStore()
    {
        return new PostgresEgressBriefStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }
}
