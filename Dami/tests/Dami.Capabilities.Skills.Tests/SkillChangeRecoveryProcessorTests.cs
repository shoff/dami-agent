using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillChangeRecoveryProcessorTests
{
    private static readonly DateTimeOffset at = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task RecoverAsync_Should_Materialize_Reload_Then_Record_Success()
    {
        SkillChangeRecord record = CreateRecord();
        var calls = new List<string>();
        var registry = new CapabilityRegistry();
        var store = new RecoveryStore(record, calls);
        var materializer = new Materializer(calls);
        var reloader = new Reloader(registry, record, calls);
        var processor = new SkillChangeRecoveryProcessor(
            store, materializer, reloader, registry, new StubTimeProvider(at));

        await processor.RecoverAsync(10, CancellationToken.None);

        Assert.Equal(["materialize", "reload", "succeed"], calls);
    }

    [Fact]
    public async Task ProcessAsync_Should_Not_Misreport_A_Success_Journal_Failure()
    {
        SkillChangeRecord record = CreateRecord();
        var calls = new List<string>();
        var registry = new CapabilityRegistry();
        var store = new RecoveryStore(record, calls) { FailSuccess = true };
        var processor = new SkillChangeRecoveryProcessor(
            store, new Materializer(calls), new Reloader(registry, record, calls),
            registry, new StubTimeProvider(at));

        await Record.ExceptionAsync(
            () => processor.ProcessAsync(record, CancellationToken.None));

        Assert.Equal(["materialize", "reload", "succeed"], calls);
    }

    [Fact]
    public async Task ProcessAsync_Should_Skip_An_Already_Succeeded_Change()
    {
        SkillChangeRecord record = CreateRecord();
        var calls = new List<string>();
        var registry = new CapabilityRegistry();
        var store = new RecoveryStore(record, calls) { Pending = false };
        var processor = new SkillChangeRecoveryProcessor(
            store, new Materializer(calls), new Reloader(registry, record, calls),
            registry, new StubTimeProvider(at));

        await processor.ProcessAsync(record, CancellationToken.None);

        Assert.Empty(calls);
    }

    private static SkillChangeRecord CreateRecord()
    {
        var document = new SkillDocument(
            Guid.NewGuid(), "compare-images", "Compare images.", "# Compare",
            ["vision"], [], new Dictionary<string, string>());
        var request = new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.SelfAudit,
            SkillChangeKind.Author, document.SkillId, null, document);
        string version = new SkillDocumentVersioner().Compute(document);
        return new SkillChangeRecord(request, "+ # Compare", version, at);
    }

    private sealed class RecoveryStore(
        SkillChangeRecord pending,
        ICollection<string> calls) : ISkillChangeRecoveryStore
    {
        public bool FailSuccess { get; init; }

        public bool Pending { get; init; } = true;

        public Task<bool> IsPendingAsync(Guid changeId, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.Pending);
        }

        public Task<IReadOnlyList<SkillChangeRecord>> FindPendingAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SkillChangeRecord>>([pending]);
        }

        public Task RecordSucceededAsync(
            SkillChangeRecord record,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            calls.Add("succeed");
            return this.FailSuccess
                ? Task.FromException(new IOException("success journal unavailable"))
                : Task.CompletedTask;
        }

        public Task RecordFailedAsync(
            SkillChangeRecord record,
            string failureCode,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            calls.Add("fail");
            return Task.CompletedTask;
        }
    }

    private sealed class Materializer(ICollection<string> calls) : ISkillChangeMaterializer
    {
        public Task ApplyAsync(SkillChangeRecord record, CancellationToken cancellationToken)
        {
            calls.Add("materialize");
            return Task.CompletedTask;
        }
    }

    private sealed class Reloader(
        ICapabilitySourceSnapshotRegistrar registrar,
        SkillChangeRecord record,
        ICollection<string> calls) : ISkillSourceReloader
    {
        public Task ReloadAsync(DateTimeOffset registeredAt, CancellationToken cancellationToken)
        {
            calls.Add("reload");
            SkillDocument document = record.Request.Replacement!;
            registrar.ReplaceSourceSnapshot(CapabilitySource.Skill,
            [
                new CapabilityEntry(
                    document.SkillId, document.Name, document.Description,
                    CapabilityKind.Skill, CapabilitySource.Skill, TrustLevel.Trusted,
                    document.Tags, null, $"skill://{document.SkillId:D}/SKILL.md",
                    document.RelatedCapabilities, record.ReplacementVersion!, registeredAt),
            ]);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
