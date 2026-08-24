using Dami.Contracts.Approvals;
using Dami.Contracts.Events;
using Dami.Persistence.Approvals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Approvals;

/// <summary>Approvals: durable, single-resolution, trace-anchored.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresApprovalServiceTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresApprovalServiceTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task RequestAsync_Should_Make_The_Request_Pending()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        await service.RequestAsync(Request(), CancellationToken.None);

        Assert.Single(await this.PendingAsync(service));
    }

    [Fact]
    public async Task RequestAsync_Should_Round_Trip_Execution_Provenance()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var parentSpanId = Guid.NewGuid();
        var request = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "media-librarian", "move files", "filesystem",
            "manifest.json", at, origin: ExecutionOrigin.ScheduledService,
            parentSpanId: parentSpanId);

        await service.RequestAsync(request, CancellationToken.None);
        var found = await service.FindAsync(request.ApprovalId, CancellationToken.None);

        Assert.Equal(ExecutionOrigin.ScheduledService, found!.Origin);
        Assert.Equal(parentSpanId, found.ParentSpanId);
    }

    [Fact]
    public async Task ResolveAsync_Should_Remove_The_Request_From_Pending()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);

        var resolved = await service.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Approved, "yes", at.AddMinutes(5), CancellationToken.None);

        Assert.Equal((true, 0), (resolved, (await this.PendingAsync(service)).Count));
    }

    [Fact]
    public async Task ResolveAsync_Should_Refuse_A_Second_Resolution()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);
        await service.ResolveAsync(request.ApprovalId, ApprovalStatus.Denied, "no", at, CancellationToken.None);

        var second = await service.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Approved, "changed my mind", at.AddHours(1), CancellationToken.None);

        Assert.False(second);
    }

    [Fact]
    public async Task ResolveAsync_Should_Not_Let_A_Denial_Become_An_Approval()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);
        await service.ResolveAsync(request.ApprovalId, ApprovalStatus.Denied, "no", at, CancellationToken.None);
        await service.ResolveAsync(request.ApprovalId, ApprovalStatus.Approved, "yes", at, CancellationToken.None);

        var found = await service.FindAsync(request.ApprovalId, CancellationToken.None);
        Assert.Equal(ApprovalStatus.Denied, found!.Status);
    }

    [Fact]
    public async Task ResolveAsync_Should_Reject_Pending_As_A_Resolution()
    {
        var service = this.CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveAsync(
            Guid.NewGuid(), ApprovalStatus.Pending, null, at, CancellationToken.None));
    }

    private static ApprovalRequest Request()
    {
        return new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "media-librarian",
            "Execute the proposed organization of 3 file(s)", "filesystem",
            "/tmp/manifest.json", at);
    }

    private async Task<List<ApprovalRequest>> PendingAsync(IApprovalService service)
    {
        var pending = new List<ApprovalRequest>();
        await foreach (var request in service.PendingAsync(CancellationToken.None))
        {
            pending.Add(request);
        }

        return pending;
    }

    private PostgresApprovalService CreateService()
    {
        return new PostgresApprovalService(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresApprovalService>.Instance);
    }
}
