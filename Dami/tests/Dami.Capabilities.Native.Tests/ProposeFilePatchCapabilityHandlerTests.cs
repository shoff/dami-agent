using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.FilePatches;

namespace Dami.Capabilities.Native.Tests;

public sealed class ProposeFilePatchCapabilityHandlerTests : IDisposable
{
    private static readonly DateTimeOffset now =
        new(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);

    private readonly string outside = Path.Combine(
        Path.GetTempPath(), "dami-patch-outside-" + Guid.NewGuid().ToString("N"));
    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-patch-proposal-" + Guid.NewGuid().ToString("N"));

    public ProposeFilePatchCapabilityHandlerTests()
    {
        Directory.CreateDirectory(this.scratch);
        Directory.CreateDirectory(this.outside);
    }

    public void Dispose()
    {
        Directory.Delete(this.scratch, recursive: true);
        Directory.Delete(this.outside, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_Should_File_Hash_Pinned_Approval_Without_Mutating_Target()
    {
        var target = Path.Combine(this.scratch, "notes.txt");
        await File.WriteAllTextAsync(target, "before");
        var store = new RecordingProposalStore();
        var handler = this.CreateHandler(store);
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "notes.txt", content = "after" });
        var request = TestCapabilityRequests.Create(arguments);

        var result = await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("before", await File.ReadAllTextAsync(target));
        AssertStoredProposal(store, request);
        Assert.Equal(store.Approval!.ApprovalId.ToString(), result.Evidence["approval_id"]);
        Assert.Equal("false", result.Evidence["target_mutated"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Propose_Create_Without_Creating_An_Absent_Target()
    {
        var target = Path.Combine(this.scratch, "new.txt");
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "new.txt", content = "created later" });

        var result = await this.CreateHandler(store).ExecuteAsync(
            TestCapabilityRequests.Create(arguments), CancellationToken.None);

        Assert.False(File.Exists(target));
        Assert.Null(store.Proposal!.ExpectedSha256);
        Assert.Equal("Create file with the reviewed proposal", store.Approval!.Action);
        Assert.Equal("absent", result.Evidence["expected_sha256"]);
        Assert.Equal("false", result.Evidence["target_mutated"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Store_The_Canonical_Root_Relative_Path()
    {
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "notes.txt"), "before");
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "unused/../notes.txt", content = "after" });

        await this.CreateHandler(store).ExecuteAsync(
            TestCapabilityRequests.Create(arguments), CancellationToken.None);

        Assert.Equal("notes.txt", store.Approval!.Resource);
        Assert.Equal("notes.txt", store.Proposal!.RelativePath);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_An_Existing_Directory_As_A_Target()
    {
        var directory = Path.Combine(this.scratch, "folder");
        Directory.CreateDirectory(directory);
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "folder", content = "not a file" });

        await Assert.ThrowsAsync<InvalidDataException>(() => this.CreateHandler(store).ExecuteAsync(
            TestCapabilityRequests.Create(arguments), CancellationToken.None));

        Assert.Null(store.Approval);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Traversal_Outside_The_Root()
    {
        var outsideFile = Path.Combine(this.outside, "secret.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        var store = new RecordingProposalStore();
        var relativeEscape = Path.GetRelativePath(this.scratch, outsideFile);
        var arguments = JsonSerializer.SerializeToElement(
            new { path = relativeEscape, content = "tamper" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this.CreateHandler(store).ExecuteAsync(
            TestCapabilityRequests.Create(arguments), CancellationToken.None));

        Assert.Null(store.Approval);
        Assert.Equal("outside", await File.ReadAllTextAsync(outsideFile));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_A_File_Symlink_That_Escapes_The_Root()
    {
        var outsideFile = Path.Combine(this.outside, "secret.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        File.CreateSymbolicLink(Path.Combine(this.scratch, "link.txt"), outsideFile);
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "link.txt", content = "tamper" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this.CreateHandler(store).ExecuteAsync(
            TestCapabilityRequests.Create(arguments), CancellationToken.None));

        Assert.Null(store.Approval);
        Assert.Equal("outside", await File.ReadAllTextAsync(outsideFile));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Bound_Replacement_Utf8_Bytes_Not_Characters()
    {
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "new.txt", content = "ééé" });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => this.CreateHandler(store, maxBytes: 5).ExecuteAsync(
                TestCapabilityRequests.Create(arguments), CancellationToken.None));

        Assert.Contains("5 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Approval);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Derive_Stable_Ids_From_The_Span_For_Retry()
    {
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "notes.txt"), "before");
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "notes.txt", content = "after" });
        var request = TestCapabilityRequests.Create(arguments);
        var handler = this.CreateHandler(store);

        await handler.ExecuteAsync(request, CancellationToken.None);
        var firstApprovalId = store.Approval!.ApprovalId;
        var firstProposalId = store.Proposal!.ProposalId;
        await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(firstApprovalId, store.Approval!.ApprovalId);
        Assert.Equal(firstProposalId, store.Proposal!.ProposalId);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Alias_The_Same_Span_Across_Traces()
    {
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "notes.txt"), "before");
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "notes.txt", content = "after" });
        var invocation = new CapabilityInvocation(Guid.NewGuid(), arguments);
        var spanId = Guid.NewGuid();
        var handler = this.CreateHandler(store);

        await handler.ExecuteAsync(
            new CapabilityExecutionRequest(
                Guid.NewGuid(), spanId, PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, invocation),
            CancellationToken.None);
        var firstApprovalId = store.Approval!.ApprovalId;
        await handler.ExecuteAsync(
            new CapabilityExecutionRequest(
                Guid.NewGuid(), spanId, PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn, invocation),
            CancellationToken.None);

        Assert.NotEqual(firstApprovalId, store.Approval!.ApprovalId);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_A_Current_File_Over_The_Byte_Bound()
    {
        var target = Path.Combine(this.scratch, "large.txt");
        await File.WriteAllTextAsync(target, "123456");
        var store = new RecordingProposalStore();
        var arguments = JsonSerializer.SerializeToElement(
            new { path = "large.txt", content = "small" });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => this.CreateHandler(store, maxBytes: 5).ExecuteAsync(
                TestCapabilityRequests.Create(arguments), CancellationToken.None));

        Assert.Contains("5 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Approval);
        Assert.Equal("123456", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public void Discovery_Should_Advertise_The_Trusted_Propose_Only_Tool()
    {
        var registrations = new NativeCapabilityDiscovery().Discover(
            typeof(ProposeFilePatchCapabilityHandler).Assembly, now);

        var registration = Assert.Single(
            registrations,
            item => item.ImplementationType == typeof(ProposeFilePatchCapabilityHandler));
        Assert.Equal("propose-file-patch", registration.Entry.Name);
        Assert.Equal(CapabilitySource.Native, registration.Entry.Source);
        Assert.Equal(TrustLevel.Trusted, registration.Entry.Trust);
        Assert.Equal("native://propose-file-patch/schema/v1", registration.Entry.SchemaReference);
        Assert.Equal(["files", "write", "approval"], registration.Entry.Tags);
    }

    private ProposeFilePatchCapabilityHandler CreateHandler(
        RecordingProposalStore store,
        int maxBytes = 1024)
    {
        return new ProposeFilePatchCapabilityHandler(
            store,
            new ProposeFilePatchCapabilityOptions
            {
                RootDirectory = this.scratch,
                MaxBytes = maxBytes,
            },
            new FixedTimeProvider(now));
    }

    private static void AssertStoredProposal(
        RecordingProposalStore store,
        CapabilityExecutionRequest request)
    {
        var approval = Assert.IsType<ApprovalRequest>(store.Approval);
        var proposal = Assert.IsType<FilePatchProposal>(store.Proposal);
        Assert.Equal(approval.ApprovalId, proposal.ApprovalId);
        Assert.Equal(request.TraceId, approval.TraceId);
        Assert.Equal(request.TraceId, proposal.TraceId);
        Assert.Equal(request.SpanId, proposal.SpanId);
        Assert.Equal("notes.txt", approval.Resource);
        Assert.Equal("notes.txt", proposal.RelativePath);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal("native:propose-file-patch", approval.RequestedBy);
        Assert.Equal("filesystem", approval.Scope);
        Assert.Equal(FilePatchProposal.HashOf("before"), proposal.ExpectedSha256);
        Assert.Equal(FilePatchProposal.HashOf("after"), proposal.ReplacementSha256);
        Assert.Equal("after", proposal.ReplacementContent);
        Assert.Equal(now, approval.RequestedAt);
        Assert.Equal(now, proposal.CreatedAt);
    }

    private sealed class RecordingProposalStore : IFilePatchProposalStore
    {
        public ApprovalRequest? Approval { get; private set; }

        public FilePatchProposal? Proposal { get; private set; }

        public Task CreateAsync(
            ApprovalRequest approval,
            FilePatchProposal proposal,
            CancellationToken cancellationToken)
        {
            this.Approval = approval;
            this.Proposal = proposal;
            return Task.CompletedTask;
        }

        public Task<FilePatchProposal?> FindByApprovalAsync(
            Guid approvalId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(this.Proposal?.ApprovalId == approvalId ? this.Proposal : null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
