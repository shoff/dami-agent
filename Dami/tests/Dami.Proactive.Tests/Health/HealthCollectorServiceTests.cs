using Dami.Contracts.Domains;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Health;

/// <summary>The collector: extract stated facts, mark examined, never surface.</summary>
public sealed class HealthCollectorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid observationId = Guid.NewGuid();

    private readonly IHealthEventStore healthStore = Substitute.For<IHealthEventStore>();
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();

    [Fact]
    public async Task RunPassAsync_Should_Record_A_Fact_The_Model_Extracted()
    {
        this.Arrange(
            "note about the heart",
            """[{"date":"2026-01-30","category":"diagnosis","description":"severe aortic stenosis"}]""");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.Received(1).RecordAsync(
            Arg.Is<HealthEvent>(e => e.Description == "severe aortic stenosis"
                && e.Category == HealthCategory.Diagnosis),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Mark_An_Observation_Examined_Even_With_No_Facts()
    {
        this.Arrange("just a note about the weather", "[]");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.Received(1).MarkExaminedAsync(observationId, Arg.Any<CancellationToken>());
        await this.healthStore.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task RunPassAsync_Should_Ignore_A_Fact_With_An_Unknown_Category()
    {
        this.Arrange(
            "note", """[{"date":"2026-01-30","category":"horoscope","description":"nonsense"}]""");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task RunPassAsync_Should_Survive_Non_Json_From_The_Model()
    {
        this.Arrange("note", "I could not find any health facts, sorry.");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
        await this.healthStore.Received(1).MarkExaminedAsync(observationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Survive_A_Failed_Extraction_And_Keep_Going()
    {
        var good = Guid.NewGuid();
        this.healthStore.UnexaminedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TwoObservationsAsync(good));
        var calls = 0;
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? throw new HttpRequestException("the response ended prematurely")
                : Task.FromResult("""[{"category":"vital","description":"BP 120/80"}]"""));

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        // The second observation still produced its fact despite the first throwing.
        await this.healthStore.Received(1).RecordAsync(
            Arg.Is<HealthEvent>(e => e.ObservationId == good), Arg.Any<CancellationToken>());
        Assert.Empty(result.Surfacings);
    }

    private static async IAsyncEnumerable<(Guid, DateOnly, string)> TwoObservationsAsync(Guid second)
    {
        yield return (Guid.NewGuid(), new DateOnly(2026, 1, 30), "first note");
        yield return (second, new DateOnly(2026, 2, 1), "second note");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RunPassAsync_Should_Reject_A_Contentless_Description()
    {
        this.Arrange("note", """[{"category":"diagnosis","description":"Cardiac diagnosis"}]""");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task RunPassAsync_Should_Keep_A_Terse_But_Specific_Fact()
    {
        // "BP 120/80" and "aortic stenosis" are short and entirely specific. An earlier
        // word-count guard discarded both; terse is not the same as empty.
        this.Arrange("note", """[{"category":"vital","description":"BP 120/80"}]""");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.Received(1).RecordAsync(
            Arg.Any<HealthEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Keep_A_Specific_Clinical_Fact()
    {
        this.Arrange(
            "note",
            """[{"category":"procedure","description":"Heart catheterization scheduled at Methodist Hospital"}]""");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.healthStore.Received(1).RecordAsync(
            Arg.Any<HealthEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_Should_Produce_No_Surfacings()
    {
        this.Arrange(
            "note", """[{"category":"vital","description":"BP 120/80"}]""");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    private void Arrange(string body, string reply)
    {
        this.healthStore.UnexaminedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OneObservationAsync(body));
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reply);
    }

    private static async IAsyncEnumerable<(Guid, DateOnly, string)> OneObservationAsync(string body)
    {
        yield return (observationId, new DateOnly(2026, 1, 30), body);
        await Task.CompletedTask;
    }

    private HealthCollectorService CreateService()
    {
        return new HealthCollectorService(
            this.healthStore, this.chatClient, Options.Create(new HealthCollectorOptions()),
            NullLogger<HealthCollectorService>.Instance);
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }
}
