using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Embedder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Embedder;

/// <summary>The pass must actually reach both indexes — a wired-but-never-called
/// belief path compiles clean and does nothing.</summary>
public sealed class EmbedderServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 18, 0, 0, TimeSpan.Zero);

    private readonly IObservationEmbeddingStore embeddingStore =
        Substitute.For<IObservationEmbeddingStore>();
    private readonly IConclusionEmbeddingStore conclusionEmbeddingStore =
        Substitute.For<IConclusionEmbeddingStore>();
    private readonly IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();

    [Fact]
    public async Task RunPassAsync_Should_Store_A_Vector_For_An_Unembedded_Belief()
    {
        var belief = NewConclusion("ships vertical slices");
        this.Arrange([belief]);

        await this.CreateService().RunPassAsync(NewContext(), CancellationToken.None);

        await this.conclusionEmbeddingStore.Received(1).StoreAsync(
            belief.ConclusionId, "test-model", Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Embed_The_Belief_Statement_Text()
    {
        this.Arrange([NewConclusion("the statement itself")]);

        await this.CreateService().RunPassAsync(NewContext(), CancellationToken.None);

        await this.embeddingClient.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<string>>(texts => texts.Contains("the statement itself")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Store_Nothing_When_All_Beliefs_Are_Indexed()
    {
        this.Arrange([]);

        await this.CreateService().RunPassAsync(NewContext(), CancellationToken.None);

        await this.conclusionEmbeddingStore.DidNotReceive().StoreAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    private void Arrange(List<Conclusion> unembedded)
    {
        this.embeddingClient.ModelId.Returns("test-model");
        this.embeddingClient
            .EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Enumerable
                .Range(0, callInfo.Arg<IReadOnlyList<string>>().Count)
                .Select(_ => new float[4]).ToList());
        this.embeddingStore
            .UnembeddedAsync("test-model", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyObservationsAsync());
        this.conclusionEmbeddingStore
            .UnembeddedAsync("test-model", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsConclusionsAsync(unembedded));
    }

    private EmbedderService CreateService()
    {
        return new EmbedderService(
            this.embeddingStore, this.conclusionEmbeddingStore, this.embeddingClient,
            Options.Create(new EmbedderOptions()), NullLogger<EmbedderService>.Instance);
    }

    private static ProactiveContext NewContext()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private static Conclusion NewConclusion(string statement)
    {
        return new Conclusion(
            Guid.NewGuid(), null, "steve", statement, 0.9, ConclusionSource.ReflectionPass, now);
    }

    private static async IAsyncEnumerable<Observation> EmptyObservationsAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<Conclusion> AsConclusionsAsync(List<Conclusion> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
