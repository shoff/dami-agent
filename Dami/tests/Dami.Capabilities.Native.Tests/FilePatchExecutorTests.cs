using Dami.Contracts.Approvals;
using Dami.Contracts.FilePatches;

namespace Dami.Capabilities.Native.Tests;

public sealed class FilePatchExecutorTests : IDisposable
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 4, 30, 0, TimeSpan.Zero);

    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-patch-executor-" + Guid.NewGuid().ToString("N"));

    public FilePatchExecutorTests()
    {
        Directory.CreateDirectory(this.scratch);
    }

    public void Dispose()
    {
        Directory.Delete(this.scratch, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Apply_The_Exact_Approved_Replacement()
    {
        var target = Path.Combine(this.scratch, "notes.txt");
        await File.WriteAllTextAsync(target, "before");
        var approval = CreateApproval(ApprovalStatus.Approved);
        var proposal = CreateProposal(approval, "notes.txt", "after", "before");
        var executor = this.CreateExecutor(approval, proposal);

        var result = await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        Assert.Equal("after", await File.ReadAllTextAsync(target));
        Assert.Equal("executed: replaced notes.txt", result);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Create_An_Approved_Absent_Target()
    {
        var target = Path.Combine(this.scratch, "new.txt");
        var approval = CreateApproval(ApprovalStatus.Approved, "new.txt");
        var proposal = CreateProposal(approval, "new.txt", "created", current: null);
        var executor = this.CreateExecutor(approval, proposal);

        var result = await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        Assert.Equal("created", await File.ReadAllTextAsync(target));
        Assert.Equal("executed: created new.txt", result);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Converge_A_Retry_After_Replacement()
    {
        var target = Path.Combine(this.scratch, "notes.txt");
        await File.WriteAllTextAsync(target, "before");
        var approval = CreateApproval(ApprovalStatus.Approved);
        var proposal = CreateProposal(approval, "notes.txt", "after", "before");
        var executor = this.CreateExecutor(approval, proposal);
        await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        var result = await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        Assert.Equal("already applied: notes.txt", result);
        Assert.Equal("after", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_NonApproved_Request()
    {
        var target = Path.Combine(this.scratch, "notes.txt");
        await File.WriteAllTextAsync(target, "before");
        var approval = CreateApproval(ApprovalStatus.Denied);
        var proposal = CreateProposal(approval, "notes.txt", "after", "before");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor(approval, proposal).ExecuteAsync(
                approval.ApprovalId, CancellationToken.None));

        Assert.Equal("before", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_Changed_Preimage()
    {
        var target = Path.Combine(this.scratch, "notes.txt");
        await File.WriteAllTextAsync(target, "changed");
        var approval = CreateApproval(ApprovalStatus.Approved);
        var proposal = CreateProposal(approval, "notes.txt", "after", "before");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor(approval, proposal).ExecuteAsync(
                approval.ApprovalId, CancellationToken.None));

        Assert.Equal("changed", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_To_Overwrite_A_Create_Target_That_Appeared()
    {
        var target = Path.Combine(this.scratch, "new.txt");
        await File.WriteAllTextAsync(target, "appeared");
        var approval = CreateApproval(ApprovalStatus.Approved, "new.txt");
        var proposal = CreateProposal(approval, "new.txt", "created", current: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor(approval, proposal).ExecuteAsync(
                approval.ApprovalId, CancellationToken.None));

        Assert.Equal("appeared", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Converge_A_Retry_After_Create()
    {
        var approval = CreateApproval(ApprovalStatus.Approved, "new.txt");
        var proposal = CreateProposal(approval, "new.txt", "created", current: null);
        var executor = this.CreateExecutor(approval, proposal);
        await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        var result = await executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None);

        Assert.Equal("already applied: new.txt", result);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_An_Oversized_Persisted_Replacement()
    {
        var target = Path.Combine(this.scratch, "new.txt");
        var approval = CreateApproval(ApprovalStatus.Approved, "new.txt");
        var proposal = CreateProposal(approval, "new.txt", new string('x', 1025), current: null);
        var executor = this.CreateExecutor(approval, proposal);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => executor.ExecuteAsync(approval.ApprovalId, CancellationToken.None));

        Assert.False(File.Exists(target));
    }

    [Theory]
    [InlineData("native:propose-file-patch", true)]
    [InlineData("frontier-brief", false)]
    public void CanExecute_Should_Own_Only_File_Patch_Approvals(
        string requestedBy,
        bool expected)
    {
        var approval = new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), requestedBy, "action", "scope", "resource", at);
        var proposal = CreateProposal(approval, "resource", "after", current: null);

        var actual = this.CreateExecutor(approval, proposal).CanExecute(approval);

        Assert.Equal(expected, actual);
    }

    private FilePatchExecutor CreateExecutor(
        ApprovalRequest approval,
        FilePatchProposal proposal)
    {
        return new FilePatchExecutor(
            new StubApprovalService(approval),
            new StubProposalStore(proposal),
            new ProposeFilePatchCapabilityOptions
            {
                RootDirectory = this.scratch,
                MaxBytes = 1024,
            });
    }

    private static ApprovalRequest CreateApproval(
        ApprovalStatus status,
        string resource = "notes.txt")
    {
        return new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), "native:propose-file-patch", "replace",
            "filesystem", resource, at, status, status == ApprovalStatus.Pending ? null : at);
    }

    private static FilePatchProposal CreateProposal(
        ApprovalRequest approval,
        string path,
        string replacement,
        string? current)
    {
        return new FilePatchProposal(
            Guid.NewGuid(), approval.ApprovalId, approval.TraceId, Guid.NewGuid(), path,
            replacement, FilePatchProposal.HashOf(replacement),
            current is null ? null : FilePatchProposal.HashOf(current), at);
    }

    private sealed class StubApprovalService(ApprovalRequest approval) : IApprovalService
    {
        public Task RequestAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<ApprovalRequest> PendingAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> ResolveAsync(
            Guid approvalId,
            ApprovalStatus resolution,
            string? note,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ApprovalRequest?> FindAsync(Guid approvalId, CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalRequest?>(approval.ApprovalId == approvalId ? approval : null);
    }

    private sealed class StubProposalStore(FilePatchProposal proposal) : IFilePatchProposalStore
    {
        public Task CreateAsync(
            ApprovalRequest approval,
            FilePatchProposal proposal,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FilePatchProposal?> FindByApprovalAsync(
            Guid approvalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FilePatchProposal?>(
                proposal.ApprovalId == approvalId ? proposal : null);
    }
}
