using Dami.Contracts.Approvals;
using Dami.Core.Approvals;
using Xunit;

namespace Dami.Core.Tests.Approvals;

public sealed class ApprovalExecutionDispatcherTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_Should_Invoke_The_Only_Matching_Handler()
    {
        var approval = CreateApproval("file-patch");
        var skipped = new StubHandler(matches: false, "skipped");
        var selected = new StubHandler(matches: true, "executed");
        var dispatcher = new ApprovalExecutionDispatcher([skipped, selected]);

        var result = await dispatcher.ExecuteAsync(approval, CancellationToken.None);

        Assert.Equal("executed", result);
        Assert.Null(skipped.Received);
        Assert.Same(approval, selected.Received);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Null_When_No_Handler_Owns_The_Approval()
    {
        var dispatcher = new ApprovalExecutionDispatcher([new StubHandler(false, "unused")]);

        var result = await dispatcher.ExecuteAsync(
            CreateApproval("manual-only"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Ambiguous_Handler_Matches()
    {
        var approval = CreateApproval("ambiguous");
        var dispatcher = new ApprovalExecutionDispatcher(
            [new StubHandler(true, "first"), new StubHandler(true, "second")]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.ExecuteAsync(approval, CancellationToken.None));

        Assert.Contains(approval.ApprovalId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static ApprovalRequest CreateApproval(string requestedBy)
    {
        return new ApprovalRequest(
            Guid.NewGuid(), Guid.NewGuid(), requestedBy, "action", "scope", "resource", at);
    }

    private sealed class StubHandler(bool matches, string result) : IApprovalExecutionHandler
    {
        public ApprovalRequest? Received { get; private set; }

        public bool CanExecute(ApprovalRequest approval) => matches;

        public Task<string> ExecuteAsync(
            ApprovalRequest approval,
            CancellationToken cancellationToken)
        {
            this.Received = approval;
            return Task.FromResult(result);
        }
    }
}
