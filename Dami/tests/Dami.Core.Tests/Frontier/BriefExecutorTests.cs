using Dami.Contracts.Approvals;
using Dami.Contracts.Briefs;
using Dami.Contracts.Models;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>C4's guarantee: only approved, only the reviewed bytes, byte-exactly.</summary>
public sealed class BriefExecutorTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 21, 0, 0, TimeSpan.Zero);
    private static readonly Guid approvalId = Guid.NewGuid();

    private readonly IApprovalService approvalService = Substitute.For<IApprovalService>();
    private readonly IEgressBriefStore briefStore = Substitute.For<IEgressBriefStore>();
    private readonly IFrontierChat frontierChat = Substitute.For<IFrontierChat>();

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_Pending_Approval()
    {
        this.Arrange(ApprovalStatus.Pending, "the brief");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor().ExecuteAsync(approvalId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_A_Denied_Approval()
    {
        this.Arrange(ApprovalStatus.Denied, "the brief");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor().ExecuteAsync(approvalId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Refuse_When_The_Bytes_No_Longer_Match_The_Hash()
    {
        this.Arrange(ApprovalStatus.Approved, "the brief", storedHash: "not-the-real-hash");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreateExecutor().ExecuteAsync(approvalId, CancellationToken.None));

        await this.frontierChat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Send_Exactly_The_Approved_Bytes()
    {
        this.Arrange(ApprovalStatus.Approved, "the reviewed brief");

        await this.CreateExecutor().ExecuteAsync(approvalId, CancellationToken.None);

        await this.frontierChat.Received(1).CompleteAsync(
            Arg.Is<FrontierPrompt>(prompt => prompt.Prompt == "the reviewed brief"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Record_The_Answer_As_Sent()
    {
        this.Arrange(ApprovalStatus.Approved, "the reviewed brief");

        await this.CreateExecutor().ExecuteAsync(approvalId, CancellationToken.None);

        await this.briefStore.Received(1).MarkSentAsync(
            Arg.Any<Guid>(), "the answer", now, Arg.Any<CancellationToken>());
    }

    private void Arrange(ApprovalStatus status, string briefText, string? storedHash = null)
    {
        this.approvalService.FindAsync(approvalId, Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest(
                approvalId, Guid.NewGuid(), "frontier-brief", "send brief", "egress", "codex",
                now, status));
        this.briefStore.FindByApprovalAsync(approvalId, Arg.Any<CancellationToken>())
            .Returns(new EgressBrief(
                Guid.NewGuid(), approvalId, Guid.NewGuid(), "question", briefText,
                storedHash ?? BriefExecutor.HashOf(briefText), now));
        this.frontierChat.CompleteAsync(Arg.Any<FrontierPrompt>(), Arg.Any<CancellationToken>())
            .Returns("the answer");
    }

    private BriefExecutor CreateExecutor()
    {
        return new BriefExecutor(
            this.approvalService, this.briefStore, this.frontierChat,
            new FakeTimeProvider(now), NullLogger<BriefExecutor>.Instance);
    }
}
