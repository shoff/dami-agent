using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillLifecycleServiceTests
{
    private static readonly DateTimeOffset at = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task ApplyAsync_Should_Write_Ahead_Before_Materialization()
    {
        SkillChangeRequest request = CreateRequest();
        var calls = new List<string>();
        var store = new ChangeStore(calls);
        var processor = new Processor(calls);
        var service = new SkillLifecycleService(
            store, processor, new SkillDocumentVersioner(), new StubTimeProvider(at));

        SkillChangeRecord applied = await service.ApplyAsync(
            request, "+ # Compare", CancellationToken.None);

        Assert.Equal(
            ("write-ahead,materialize",
                new SkillDocumentVersioner().Compute(request.Replacement!)),
            (string.Join(',', calls), applied.ReplacementVersion));
    }

    private static SkillChangeRequest CreateRequest()
    {
        var document = new SkillDocument(
            Guid.NewGuid(), "compare-images", "Compare images.", "# Compare",
            ["vision"], [], new Dictionary<string, string>());
        return new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.SelfAudit,
            SkillChangeKind.Author, document.SkillId, null, document);
    }

    private sealed class ChangeStore(ICollection<string> calls) : ISkillChangeStore
    {
        public Task<SkillChangeRecord> CreateAsync(
            SkillChangeRecord record,
            CancellationToken cancellationToken)
        {
            calls.Add("write-ahead");
            return Task.FromResult(record);
        }

        public Task<SkillChangeRecord?> FindAsync(
            Guid changeId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<SkillChangeRecord?>(null);
        }
    }

    private sealed class Processor(ICollection<string> calls) : ISkillChangeProcessor
    {
        public Task ProcessAsync(SkillChangeRecord record, CancellationToken cancellationToken)
        {
            calls.Add("materialize");
            return Task.CompletedTask;
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
