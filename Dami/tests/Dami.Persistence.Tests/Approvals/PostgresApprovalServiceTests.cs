using Dami.Contracts.Approvals;
using Dami.Contracts.Events;
using Dami.Persistence.Approvals;
using Dami.Persistence.Events;
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
    public async Task RequestAsync_Should_Append_ApprovalRequested_To_The_Same_Trace()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var parentSpanId = Guid.NewGuid();
        var request = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "media-librarian", "move files", "filesystem",
            "manifest.json", at, origin: ExecutionOrigin.ScheduledService,
            parentSpanId: parentSpanId);

        await service.RequestAsync(request, CancellationToken.None);
        var events = await this.ReplayAsync(request.TraceId);

        var requested = Assert.Single(events);
        Assert.Equal(ExecutionEventType.ApprovalRequested, requested.Type);
        Assert.Equal(request.ApprovalId, requested.SpanId);
        Assert.Equal(request.ParentSpanId, requested.ParentSpanId);
        Assert.Equal(request.Origin, requested.Origin);
    }

    [Fact]
    public async Task RequestAsync_Should_Roll_Back_When_The_Request_Event_Fails()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA, ExecutionEventType.ApprovalRequested);

        await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => service.RequestAsync(request, CancellationToken.None));
        Assert.Null(await service.FindAsync(request.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_Should_Reject_A_Conflicting_Immutable_Replay()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);
        var conflicting = new ApprovalRequest(
            request.ApprovalId, request.TraceId, request.RequestedBy, "different action",
            request.Scope, request.Resource, request.RequestedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RequestAsync(conflicting, CancellationToken.None));

        Assert.Equal(request, await service.FindAsync(request.ApprovalId, CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_Should_Reject_An_Already_Resolved_Request()
    {
        var service = this.CreateService();
        var request = Request();
        var resolved = new ApprovalRequest(
            request.ApprovalId, request.TraceId, request.RequestedBy, request.Action,
            request.Scope, request.Resource, request.RequestedAt, ApprovalStatus.Approved,
            resolvedAt: at.AddMinutes(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RequestAsync(resolved, CancellationToken.None));
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
    public async Task ResolveAsync_Should_Append_ApprovalResolved_To_The_Same_Span()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);

        await service.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Approved, "yes", at.AddMinutes(5), CancellationToken.None);
        var events = await this.ReplayAsync(request.TraceId);

        var resolved = Assert.Single(events, item => item.Type == ExecutionEventType.ApprovalResolved);
        Assert.Equal(request.ApprovalId, resolved.SpanId);
        Assert.Equal(ExecutionStatus.Succeeded, resolved.Status);
        Assert.Equal(at.AddMinutes(5), resolved.OccurredAt);
    }

    [Fact]
    public async Task ResolveAsync_Should_Roll_Back_When_The_Resolved_Event_Fails()
    {
        await this.fixture.ResetAsync();
        var service = this.CreateService();
        var request = Request();
        await service.RequestAsync(request, CancellationToken.None);
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA, ExecutionEventType.ApprovalResolved);

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => service.ResolveAsync(
            request.ApprovalId, ApprovalStatus.Approved, "yes", at, CancellationToken.None));
        Assert.Equal(ApprovalStatus.Pending,
            (await service.FindAsync(request.ApprovalId, CancellationToken.None))!.Status);
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
        var events = await this.ReplayAsync(request.TraceId);

        Assert.False(second);
        Assert.Single(events, item => item.Type == ExecutionEventType.ApprovalResolved);
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

    private async Task<List<ExecutionEvent>> ReplayAsync(Guid traceId)
    {
        var events = new List<ExecutionEvent>();
        var store = new PostgresExecutionEventStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresExecutionEventStore>.Instance);
        await foreach (var executionEvent in store.ReplayAsync(traceId, CancellationToken.None))
        {
            events.Add(executionEvent);
        }

        return events;
    }

    private PostgresApprovalService CreateService()
    {
        return new PostgresApprovalService(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresApprovalService>.Instance);
    }
}
