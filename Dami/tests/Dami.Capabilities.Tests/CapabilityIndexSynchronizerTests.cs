using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Models;

namespace Dami.Capabilities.Tests;

public sealed class CapabilityIndexSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_Should_Index_Changed_Entries_And_Remove_Stale_Ones()
    {
        var unchanged = CreateTool("00000000-0000-0000-0000-000000000001", "unchanged", "1.0.0");
        var changed = CreateTool("00000000-0000-0000-0000-000000000002", "changed", "2.0.0");
        var added = CreateTool("00000000-0000-0000-0000-000000000003", "added", "1.0.0");
        var staleId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var registry = new CapabilityRegistry();
        registry.Register(changed);
        registry.Register(added);
        registry.Register(unchanged);
        var store = new RecordingEmbeddingStore(new Dictionary<Guid, string>
        {
            [unchanged.CapabilityId] = "1.0.0",
            [changed.CapabilityId] = "1.0.0",
            [staleId] = "1.0.0",
        });
        var embeddingClient = new RecordingEmbeddingClient();
        var synchronizer = new CapabilityIndexSynchronizer(registry, store, embeddingClient);

        CapabilityIndexSyncResult result = await synchronizer
            .SynchronizeAsync(CancellationToken.None);

        Assert.Equal([changed.Description, added.Description], embeddingClient.LastTexts);
        Assert.Equal([changed.CapabilityId, added.CapabilityId], store.UpsertedIds);
        Assert.Equal([staleId], store.RemovedIds);
        Assert.Equal(new CapabilityIndexSyncResult(2, 1), result);
    }

    [Fact]
    public async Task SynchronizeAsync_Should_Serialize_Concurrent_First_Use()
    {
        var entry = CreateTool("00000000-0000-0000-0000-000000000001", "tool", "1.0.0");
        var registry = new CapabilityRegistry();
        registry.Register(entry);
        var store = new RecordingEmbeddingStore([]);
        var embeddingClient = new RecordingEmbeddingClient(TimeSpan.FromMilliseconds(100));
        var synchronizer = new CapabilityIndexSynchronizer(registry, store, embeddingClient);

        Task<CapabilityIndexSyncResult> first = synchronizer.SynchronizeAsync(CancellationToken.None);
        Task<CapabilityIndexSyncResult> second = synchronizer.SynchronizeAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(1, embeddingClient.CallCount);
        Assert.Single(store.UpsertedIds);
    }

    private static CapabilityEntry CreateTool(string capabilityId, string name, string version)
    {
        return new CapabilityEntry(
            Guid.Parse(capabilityId),
            name,
            $"Description for {name}.",
            CapabilityKind.Tool,
            CapabilitySource.Native,
            TrustLevel.Trusted,
            [],
            $"native://{name}/schema",
            null,
            [],
            version,
            DateTimeOffset.UnixEpoch);
    }

    private sealed class RecordingEmbeddingClient : IEmbeddingClient
    {
        private readonly TimeSpan delay;
        private int callCount;

        public RecordingEmbeddingClient(TimeSpan delay = default)
        {
            this.delay = delay;
        }

        public string ModelId => "test-model";

        public IReadOnlyList<string> LastTexts { get; private set; } = [];

        public int CallCount => this.callCount;

        public async Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.callCount);
            this.LastTexts = texts;
            await Task.Delay(this.delay, cancellationToken);
            IReadOnlyList<float[]> vectors = texts.Select((_, index) => new[] { (float)index }).ToArray();
            return vectors;
        }
    }

    private sealed class RecordingEmbeddingStore : ICapabilityEmbeddingStore
    {
        private readonly ConcurrentDictionary<Guid, string> versions;

        public RecordingEmbeddingStore(Dictionary<Guid, string> versions)
        {
            this.versions = new ConcurrentDictionary<Guid, string>(versions);
        }

        public ConcurrentQueue<Guid> UpsertedIds { get; } = [];

        public ConcurrentQueue<Guid> RemovedIds { get; } = [];

        public Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(
            string embeddingModel,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<Guid, string> snapshot = new ReadOnlyDictionary<Guid, string>(
                new Dictionary<Guid, string>(this.versions));
            return Task.FromResult(snapshot);
        }

        public Task UpsertAsync(
            Guid capabilityId,
            string capabilityVersion,
            string embeddingModel,
            float[] embedding,
            CancellationToken cancellationToken)
        {
            this.UpsertedIds.Enqueue(capabilityId);
            this.versions[capabilityId] = capabilityVersion;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            Guid capabilityId,
            string embeddingModel,
            CancellationToken cancellationToken)
        {
            this.RemovedIds.Enqueue(capabilityId);
            this.versions.TryRemove(capabilityId, out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<(Guid CapabilityId, double Distance)> NearestAsync(
            float[] queryEmbedding,
            string embeddingModel,
            int limit,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
