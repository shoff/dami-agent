using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedToolRecoveryProcessorTests
{
    [Fact]
    public async Task RecoverAsync_Should_Activate_Then_Record_First_Success_Async()
    {
        ToolActivationRecoveryItem item = CreateItem(isActivated: false);
        var calls = new List<string>();
        var processor = new SandboxedToolRecoveryProcessor(
            new RecoverySource(item), Coordinator(calls));

        ToolActivationRecoverySummary summary = await processor.RecoverAsync(
            10, CancellationToken.None);

        Assert.Equal(["activate", "succeed"], calls);
        Assert.Equal(new ToolActivationRecoverySummary(1, 1, 0), summary);
    }

    [Fact]
    public async Task RecoverAsync_Should_Record_A_Failed_First_Activation_Async()
    {
        ToolActivationRecoveryItem item = CreateItem(isActivated: false);
        var calls = new List<string>();
        var processor = new SandboxedToolRecoveryProcessor(
            new RecoverySource(item), Coordinator(calls, failing: true));

        ToolActivationRecoverySummary summary = await processor.RecoverAsync(
            10, CancellationToken.None);

        Assert.Equal(["activate", "fail"], calls);
        Assert.Equal(new ToolActivationRecoverySummary(1, 0, 1), summary);
    }

    [Fact]
    public async Task RecoverAsync_Should_Serialize_Recovery_Source_Snapshots_Async()
    {
        var source = new ConcurrentRecoverySource();
        var calls = new List<string>();
        var processor = new SandboxedToolRecoveryProcessor(
            source, Coordinator(calls));

        await Task.WhenAll(
            processor.RecoverAsync(10, CancellationToken.None),
            processor.RecoverAsync(10, CancellationToken.None));

        Assert.Equal(1, source.MaxConcurrent);
    }

    private static IToolActivationCoordinator Coordinator(
        ICollection<string> calls,
        bool failing = false)
    {
        ISandboxedToolActivator activator = failing
            ? new FailingActivator(calls)
            : new Activator(calls);
        return new SandboxedToolActivationCoordinator(
            activator, new ActivationStore(calls),
            new StubTimeProvider(DateTimeOffset.UnixEpoch));
    }

    private static ToolActivationRecoveryItem CreateItem(bool isActivated)
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "recoverable", "Recover this tool.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["recovery"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "Recovery must converge.", [Guid.NewGuid()], ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        var proposal = new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), request.ProposalId, artifact.Version,
            new string('a', 64), "tests_passed=1", DateTimeOffset.UnixEpoch);
        return new ToolActivationRecoveryItem(
            Guid.NewGuid(), proposal, verification, isActivated);
    }

    private sealed class RecoverySource(ToolActivationRecoveryItem item)
        : IToolActivationRecoverySource
    {
        public Task<IReadOnlyList<ToolActivationRecoveryItem>> FindAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ToolActivationRecoveryItem>>([item]);
        }
    }

    private sealed class ConcurrentRecoverySource : IToolActivationRecoverySource
    {
        private readonly object gate = new();
        private int current;

        public int MaxConcurrent { get; private set; }

        public async Task<IReadOnlyList<ToolActivationRecoveryItem>> FindAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            lock (this.gate)
            {
                this.current++;
                this.MaxConcurrent = Math.Max(this.MaxConcurrent, this.current);
            }

            await Task.Delay(100, cancellationToken);
            lock (this.gate)
            {
                this.current--;
            }

            return [];
        }
    }

    private sealed class Activator(ICollection<string> calls) : ISandboxedToolActivator
    {
        public Task ActivateAsync(
            ToolActivationRecoveryItem item,
            CancellationToken cancellationToken)
        {
            calls.Add("activate");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingActivator(ICollection<string> calls) : ISandboxedToolActivator
    {
        public Task ActivateAsync(
            ToolActivationRecoveryItem item,
            CancellationToken cancellationToken)
        {
            calls.Add("activate");
            return Task.FromException(new IOException("materialization failed"));
        }
    }

    private sealed class ActivationStore(ICollection<string> calls) : IToolActivationStore
    {
        public Task<ToolActivationOutcome> RecordAsync(
            ToolActivationOutcome outcome,
            CancellationToken cancellationToken)
        {
            calls.Add(outcome.Status == ToolActivationStatus.Activated ? "succeed" : "fail");
            return Task.FromResult(outcome);
        }

        public Task<ToolActivationOutcome?> FindActivatedAsync(
            Guid promotionId,
            CancellationToken cancellationToken) => Task.FromResult<ToolActivationOutcome?>(null);
    }

    private sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
