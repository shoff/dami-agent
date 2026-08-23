using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Reflection;

/// <summary>The model proposes; the service disposes.</summary>
public sealed class ReflectionServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 3, 0, 0, TimeSpan.Zero);

    private readonly IObservationCorpus observationCorpus = Substitute.For<IObservationCorpus>();
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();
    private readonly List<Observation> observations = [];

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_Below_The_Observation_Floor()
    {
        this.Observe("one thing happened");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Not_Call_The_Model_Below_The_Floor()
    {
        this.Observe("one thing happened");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.chatClient.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Record_A_Well_Formed_Proposal()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"tends to work in long focused bursts late at night","confidence":0.7,"supporting":[1,3]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Map_Provenance_Numbers_To_Observation_Ids()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"a pattern","confidence":0.7,"supporting":[1,3]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            new[] { this.observations[0].ObservationId, this.observations[2].ObservationId },
            result.Conclusions[0].SupportingObservations);
    }

    [Fact]
    public async Task RunPassAsync_Should_Discard_A_Proposal_With_No_Provenance()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"an unsupported assertion","confidence":0.9,"supporting":[]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Discard_A_Low_Confidence_Proposal()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"a shaky guess","confidence":0.2,"supporting":[1]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_When_The_Model_Says_Nothing()
    {
        this.ObserveThree();
        this.ModelSays("nothing");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Survive_Garbage_From_The_Model()
    {
        this.ObserveThree();
        this.ModelSays("{ not json at all");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunPassAsync_Should_Ignore_Out_Of_Range_Provenance_Numbers()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"cites the void","confidence":0.8,"supporting":[99]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Never_Surface()
    {
        this.ObserveThree();
        this.ModelSays("""{"statement":"a pattern","confidence":0.99,"supporting":[1]}""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    private void Observe(string body)
    {
        this.observations.Add(new Observation(Guid.NewGuid(), now.AddDays(-1), "cli-note", body));
    }

    private void ObserveThree()
    {
        this.Observe("worked on the transport codec past midnight");
        this.Observe("skipped the workshop session again");
        this.Observe("kept coding until three in the morning");
    }

    private void ModelSays(string reply)
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reply);
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private ReflectionService CreateService()
    {
        this.observationCorpus.BetweenAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.observations));

        return new ReflectionService(
            this.observationCorpus, this.chatClient, Options.Create(new ReflectionOptions()),
            new FakeTimeProvider(now), NullLogger<ReflectionService>.Instance);
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
