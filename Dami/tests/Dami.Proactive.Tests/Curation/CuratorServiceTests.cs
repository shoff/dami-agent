using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Curation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Curation;

/// <summary>A bad rewrite costs one note; a dead sidecar must not cost the whole batch.</summary>
public sealed class CuratorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 16, 3, 0, 0, TimeSpan.Zero);

    private readonly IObservationCurationStore curationStore = Substitute.For<IObservationCurationStore>();
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();
    private readonly List<Observation> uncurated = [];

    [Fact]
    public async Task RunPassAsync_Should_Store_An_Acceptable_Rewrite()
    {
        this.Uncurated("As of 2026-03-02, the user primed the fuselage halves.");
        this.ModelSays("Steve primed the fuselage halves.");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.curationStore.Received(1).CurateAsync(
            this.uncurated[0].ObservationId, "Steve primed the fuselage halves.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Keep_Going_When_One_Rewrite_Is_Garbage()
    {
        this.Uncurated("first note about Steve");
        this.Uncurated("second note about Steve");
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("garbage"),
                _ => "Steve wrote a second note.");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.chatClient.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Stop_The_Pass_When_The_Sidecar_Is_Unreachable()
    {
        this.Uncurated("first note about Steve");
        this.Uncurated("second note about Steve");
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("the response ended prematurely"));

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.chatClient.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Complete_Quietly_When_The_Sidecar_Is_Unreachable()
    {
        this.Uncurated("first note about Steve");
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("connection refused"));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
    }

    private void Uncurated(string body)
    {
        this.uncurated.Add(new Observation(Guid.NewGuid(), now.AddDays(-1), "hermes-memory", body));
    }

    private void ModelSays(string reply)
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(reply);
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private CuratorService CreateService()
    {
        this.curationStore.UncuratedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.uncurated));

        return new CuratorService(
            this.curationStore, this.chatClient, Options.Create(new CuratorOptions()),
            NullLogger<CuratorService>.Instance);
    }

    private static async IAsyncEnumerable<Observation> AsAsync(List<Observation> observations)
    {
        foreach (var observation in observations)
        {
            yield return observation;
        }

        await Task.CompletedTask;
    }
}
