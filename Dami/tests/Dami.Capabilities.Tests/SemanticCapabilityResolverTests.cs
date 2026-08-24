using System.Runtime.CompilerServices;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Models;

namespace Dami.Capabilities.Tests;

public sealed class SemanticCapabilityResolverTests
{
    [Fact]
    public async Task ResolveAsync_Should_Rerank_Ann_Candidates_Then_Expand_The_Selected_Skill()
    {
        var calls = new List<string>();
        var relatedTool = CreateTool("00000000-0000-0000-0000-000000000001", "compare-images");
        var selectedSkill = CreateSkill(
            "00000000-0000-0000-0000-000000000002", "image-workflow", relatedTool.CapabilityId);
        var irrelevantTool = CreateTool("00000000-0000-0000-0000-000000000003", "read-text");
        var registry = new CapabilityRegistry();
        registry.Register(relatedTool);
        registry.Register(selectedSkill);
        registry.Register(irrelevantTool);
        var embeddingClient = new RecordingEmbeddingClient(calls);
        var embeddingStore = new CandidateEmbeddingStore(
            calls, [irrelevantTool.CapabilityId, selectedSkill.CapabilityId]);
        var reranker = new RecordingRerankClient(calls, [1, 0]);
        var resolver = new SemanticCapabilityResolver(
            new RecordingSynchronizer(calls),
            embeddingClient,
            embeddingStore,
            reranker,
            registry,
            new CapabilityBundleExpander(registry),
            new CapabilityRetrievalOptions { CandidateLimit = 2, ResultLimit = 1 });

        CapabilityBundle bundle = await resolver.ResolveAsync(
            "compare two images", PrivacyClass.LocalOnly, CancellationToken.None);

        AssertResolved(calls, embeddingClient, reranker, irrelevantTool, selectedSkill, relatedTool, bundle);
    }

    [Fact]
    public async Task ResolveAsync_Should_Return_An_Empty_Bundle_Without_Calling_The_Reranker()
    {
        var calls = new List<string>();
        var registry = new CapabilityRegistry();
        var reranker = new RecordingRerankClient(calls, []);
        var resolver = CreateResolver(calls, registry, reranker, []);

        CapabilityBundle bundle = await resolver.ResolveAsync(
            "unknown intent", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Empty(bundle.Capabilities);
        Assert.Equal(0, reranker.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Should_Reject_An_Out_Of_Range_Reranker_Index()
    {
        var calls = new List<string>();
        var tool = CreateTool("00000000-0000-0000-0000-000000000001", "compare-images");
        var registry = new CapabilityRegistry();
        registry.Register(tool);
        var reranker = new RecordingRerankClient(calls, [1]);
        var resolver = CreateResolver(calls, registry, reranker, [tool.CapabilityId]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(
                "compare images", PrivacyClass.LocalOnly, CancellationToken.None));

        Assert.Contains("index 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_Should_Reject_An_Unknown_Privacy_Class()
    {
        var calls = new List<string>();
        var registry = new CapabilityRegistry();
        var resolver = CreateResolver(calls, registry, new RecordingRerankClient(calls, []), []);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => resolver.ResolveAsync(
                "unknown intent", (PrivacyClass)99, CancellationToken.None));

        Assert.Equal("privacy", exception.ParamName);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task ResolveAsync_Should_Exclude_Untrusted_Mcp_Before_LocalOnly_Reranking()
    {
        var calls = new List<string>();
        var trusted = CreateTool("00000000-0000-0000-0000-000000000001", "read-text");
        var untrusted = new CapabilityEntry(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "remote-tool", "Untrusted remote description.", CapabilityKind.Tool,
            CapabilitySource.Mcp, TrustLevel.Untrusted, [], "mcp://remote/schema",
            null, [], "1", DateTimeOffset.UnixEpoch);
        var registry = new CapabilityRegistry();
        registry.Register(trusted);
        registry.Register(untrusted);
        var reranker = new RecordingRerankClient(calls, [0]);
        var resolver = CreateResolver(
            calls, registry, reranker, [untrusted.CapabilityId, trusted.CapabilityId]);

        CapabilityBundle bundle = await resolver.ResolveAsync(
            "read text", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Equal([trusted.Description], reranker.Candidates);
        Assert.Equal([trusted], bundle.Capabilities);
    }

    [Fact]
    public async Task ResolveAsync_Should_Allow_Untrusted_Mcp_For_Egressable_Intent()
    {
        var calls = new List<string>();
        var untrusted = new CapabilityEntry(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "remote-tool", "Safe summary.", CapabilityKind.Tool,
            CapabilitySource.Mcp, TrustLevel.Untrusted, [], "mcp://remote/schema",
            null, [], "1", DateTimeOffset.UnixEpoch);
        var registry = new CapabilityRegistry();
        registry.Register(untrusted);
        var reranker = new RecordingRerankClient(calls, [0]);
        var resolver = CreateResolver(calls, registry, reranker, [untrusted.CapabilityId]);

        CapabilityBundle bundle = await resolver.ResolveAsync(
            "remote task", PrivacyClass.Egressable, CancellationToken.None);

        Assert.Equal([untrusted.Description], reranker.Candidates);
        Assert.Equal([untrusted], bundle.Capabilities);
    }

    [Fact]
    public async Task ResolveAsync_Should_Exclude_Untrusted_Mcp_During_LocalOnly_Expansion()
    {
        var calls = new List<string>();
        var untrusted = new CapabilityEntry(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "remote-tool", "Safe summary.", CapabilityKind.Tool,
            CapabilitySource.Mcp, TrustLevel.Untrusted, [], "mcp://remote/schema",
            null, [], "1", DateTimeOffset.UnixEpoch);
        var skill = CreateSkill(
            "00000000-0000-0000-0000-000000000002", "workflow", untrusted.CapabilityId);
        var registry = new CapabilityRegistry();
        registry.Register(untrusted);
        registry.Register(skill);
        var resolver = CreateResolver(
            calls, registry, new RecordingRerankClient(calls, [0]), [skill.CapabilityId]);

        CapabilityBundle bundle = await resolver.ResolveAsync(
            "use workflow", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Equal([skill], bundle.Capabilities);
    }

    private static SemanticCapabilityResolver CreateResolver(
        List<string> calls,
        CapabilityRegistry registry,
        RecordingRerankClient reranker,
        IReadOnlyList<Guid> candidateIds)
    {
        return new SemanticCapabilityResolver(
            new RecordingSynchronizer(calls),
            new RecordingEmbeddingClient(calls),
            new CandidateEmbeddingStore(calls, candidateIds),
            reranker,
            registry,
            new CapabilityBundleExpander(registry),
            new CapabilityRetrievalOptions { CandidateLimit = 2, ResultLimit = 1 });
    }

    private static void AssertResolved(
        IReadOnlyList<string> calls,
        RecordingEmbeddingClient embeddingClient,
        RecordingRerankClient reranker,
        CapabilityEntry irrelevantTool,
        CapabilityEntry selectedSkill,
        CapabilityEntry relatedTool,
        CapabilityBundle bundle)
    {
        Assert.Equal(["sync", "embed", "ann", "rerank"], calls);
        Assert.Equal(["compare two images"], embeddingClient.Texts);
        Assert.Equal("compare two images", reranker.Query);
        Assert.Equal([irrelevantTool.Description, selectedSkill.Description], reranker.Candidates);
        Assert.Equal([selectedSkill, relatedTool], bundle.Capabilities);
        Assert.Equal("compare two images", bundle.Name);
    }

    private static CapabilityEntry CreateTool(string capabilityId, string name)
    {
        return CreateEntry(capabilityId, name, CapabilityKind.Tool, $"native://{name}/schema", null, []);
    }

    private static CapabilityEntry CreateSkill(string capabilityId, string name, Guid relatedToolId)
    {
        return CreateEntry(capabilityId, name, CapabilityKind.Skill, null, $"skill://{name}", [relatedToolId]);
    }

    private static CapabilityEntry CreateEntry(
        string capabilityId,
        string name,
        CapabilityKind kind,
        string? schemaReference,
        string? bodyReference,
        IReadOnlyList<Guid> relatedCapabilities)
    {
        return new CapabilityEntry(
            Guid.Parse(capabilityId),
            name,
            $"Description for {name}.",
            kind,
            kind == CapabilityKind.Skill ? CapabilitySource.Skill : CapabilitySource.Native,
            TrustLevel.Trusted,
            [],
            schemaReference,
            bodyReference,
            relatedCapabilities,
            "1.0.0",
            DateTimeOffset.UnixEpoch);
    }

    private sealed class RecordingSynchronizer(List<string> calls) : ICapabilityIndexSynchronizer
    {
        public Task<CapabilityIndexSyncResult> SynchronizeAsync(CancellationToken cancellationToken)
        {
            calls.Add("sync");
            return Task.FromResult(new CapabilityIndexSyncResult(0, 0));
        }
    }

    private sealed class RecordingEmbeddingClient(List<string> calls) : IEmbeddingClient
    {
        public string ModelId => "test-model";

        public IReadOnlyList<string> Texts { get; private set; } = [];

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            calls.Add("embed");
            this.Texts = texts;
            return Task.FromResult<IReadOnlyList<float[]>>([new float[] { 42f }]);
        }
    }

    private sealed class RecordingRerankClient(
        List<string> calls,
        IReadOnlyList<int> order) : IRerankClient
    {
        private int callCount;

        public IReadOnlyList<string> Candidates { get; private set; } = [];

        public int CallCount => this.callCount;

        public string? Query { get; private set; }

        public Task<IReadOnlyList<int>> RankAsync(
            string query,
            IReadOnlyList<string> candidates,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.callCount);
            calls.Add("rerank");
            this.Query = query;
            this.Candidates = candidates;
            return Task.FromResult(order);
        }
    }

    private sealed class CandidateEmbeddingStore(
        List<string> calls,
        IReadOnlyList<Guid> candidateIds) : ICapabilityEmbeddingStore
    {
        public Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(
            string embeddingModel,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertAsync(
            Guid capabilityId,
            string capabilityVersion,
            string embeddingModel,
            float[] embedding,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(
            Guid capabilityId,
            string embeddingModel,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async IAsyncEnumerable<(Guid CapabilityId, double Distance)> NearestAsync(
            float[] queryEmbedding,
            string embeddingModel,
            int limit,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            calls.Add("ann");
            Assert.Equal([42f], queryEmbedding);
            Assert.Equal("test-model", embeddingModel);
            Assert.Equal(2, limit);
            foreach (Guid candidateId in candidateIds)
            {
                yield return (candidateId, 0.1);
            }

            await Task.CompletedTask;
        }
    }
}
