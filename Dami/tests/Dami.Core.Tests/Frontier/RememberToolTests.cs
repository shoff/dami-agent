using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class RememberToolTests
{
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();
    private readonly IEmbeddingClient embeddings = Substitute.For<IEmbeddingClient>();
    private readonly IObservationEmbeddingStore embeddingStore = Substitute.For<IObservationEmbeddingStore>();

    public RememberToolTests()
    {
        this.embeddings.ModelId.Returns("bge-m3");
        this.embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f, 0.2f }]);
    }

    private RememberTool Subject() => new(
        this.corpus, this.embeddings, this.embeddingStore, TimeProvider.System, NullLogger<RememberTool>.Instance);

    [Fact]
    public async Task Should_Record_An_Observation_With_Its_Provenance()
    {
        var trace = Guid.NewGuid();

        var result = await this.Subject().RememberAsync(
            trace, "discord:1", "Steve's dentist is Dr. Park.", CancellationToken.None);

        Assert.True(result.Success);
        await this.corpus.Received(1).RecordAsync(
            Arg.Is<Observation>(observation =>
                observation.Source == RememberTool.SOURCE
                && observation.Body == "Steve's dentist is Dr. Park."
                && observation.Metadata!["trace"] == trace.ToString("N")
                && observation.Metadata["channel"] == "discord:1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Embed_It_Now_So_Recall_Finds_It_Before_Tonight()
    {
        await this.Subject().RememberAsync(Guid.NewGuid(), "gui", "a fact", CancellationToken.None);

        await this.embeddingStore.Received(1).StoreAsync(
            Arg.Any<Guid>(), "bge-m3", Arg.Is<float[]>(vector => vector.Length == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Embedding_Failure_Should_Still_Count_As_Saved()
    {
        // The nightly embedder will index it; the note itself is durable already.
        this.embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<float[]>>>(_ => throw new HttpRequestException("TEI down"));

        var result = await this.Subject().RememberAsync(Guid.NewGuid(), "gui", "a fact", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("tonight", result.Text, StringComparison.Ordinal);
        await this.corpus.Received(1).RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Empty_Note_Should_Be_Refused_Before_Anything_Is_Written()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().RememberAsync(Guid.NewGuid(), "gui", "  ", CancellationToken.None));

        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }
}
