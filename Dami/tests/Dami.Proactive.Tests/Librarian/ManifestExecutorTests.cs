using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Proactive.Librarian;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Librarian;

/// <summary>The executor: only Approved manifests run, moves never overwrite.</summary>
public sealed class ManifestExecutorTests : IDisposable
{
    private static readonly DateTimeOffset at = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    private readonly IApprovalService approvalService = Substitute.For<IApprovalService>();
    private readonly string scratch;

    public ManifestExecutorTests()
    {
        this.scratch = Path.Combine(Path.GetTempPath(), "dami-executor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.scratch);
    }

    public void Dispose()
    {
        Directory.Delete(this.scratch, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_Pending_Approval()
    {
        var approvalId = await this.ArrangeAsync(ApprovalStatus.Pending, "a.jpg");
        var executor = this.CreateExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(approvalId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_Denied_Approval()
    {
        var approvalId = await this.ArrangeAsync(ApprovalStatus.Denied, "a.jpg");
        var executor = this.CreateExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(approvalId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Move_Files_For_An_Approved_Manifest()
    {
        var approvalId = await this.ArrangeAsync(ApprovalStatus.Approved, "a.jpg");
        var executor = this.CreateExecutor();

        var (moved, _) = await executor.ExecuteAsync(approvalId, CancellationToken.None);

        Assert.Equal(
            (1, true, false),
            (moved,
             File.Exists(Path.Combine(this.scratch, "photos", "a.jpg")),
             File.Exists(Path.Combine(this.scratch, "a.jpg"))));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Skip_Rather_Than_Overwrite()
    {
        var approvalId = await this.ArrangeAsync(ApprovalStatus.Approved, "a.jpg");
        Directory.CreateDirectory(Path.Combine(this.scratch, "photos"));
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "photos", "a.jpg"), "already here");
        var executor = this.CreateExecutor();

        var (moved, skipped) = await executor.ExecuteAsync(approvalId, CancellationToken.None);

        Assert.Equal((0, 1, "already here"),
            (moved, skipped, await File.ReadAllTextAsync(Path.Combine(this.scratch, "photos", "a.jpg"))));
    }

    private async Task<Guid> ArrangeAsync(ApprovalStatus status, string fileName)
    {
        var source = Path.Combine(this.scratch, fileName);
        await File.WriteAllTextAsync(source, "content");

        var manifest = new MediaLibrarianService.Manifest(
            at, "PROPOSAL ONLY",
            [new MediaLibrarianService.MoveProposal(
                source, Path.Combine(this.scratch, "photos", fileName), "photos")]);
        var manifestPath = Path.Combine(this.scratch, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var approvalId = Guid.NewGuid();
        this.approvalService.FindAsync(approvalId, Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest(
                approvalId, Guid.NewGuid(), "media-librarian", "execute", "filesystem",
                manifestPath, at, status,
                status == ApprovalStatus.Pending ? null : at.AddMinutes(1),
                status == ApprovalStatus.Pending ? null : "note"));
        return approvalId;
    }

    private ManifestExecutor CreateExecutor()
    {
        return new ManifestExecutor(this.approvalService, NullLogger<ManifestExecutor>.Instance);
    }
}
