using System.Net;
using System.Net.Http.Json;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

/// <summary>
/// The surfacing feedback loop, which shipped broken: the reaction was stored but the
/// item stayed Pending, so the list re-rendered identically and the click looked dead.
/// </summary>
public sealed class SurfacingEndpointsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid surfacingId = Guid.NewGuid();

    private readonly ISurfacingQueue queue = Substitute.For<ISurfacingQueue>();
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();

    public SurfacingEndpointsTests()
    {
        this.queue.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SurfacingsAsync(Worth("a talk on pgvector internals")));
    }

    [Fact]
    public async Task PostFeedback_Should_Deliver_The_Surfacing_So_It_Leaves_The_Queue()
    {
        using var response = await this.RateAsync(Short(surfacingId), "good");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await this.queue.Received(1).DeliverAsync(
            surfacingId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostFeedback_Should_Record_The_Reaction()
    {
        using var response = await this.RateAsync(Short(surfacingId), "bad");

        await this.queue.Received(1).RecordFeedbackAsync(
            surfacingId, "bad", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostFeedback_Should_Join_The_Reaction_To_The_Corpus()
    {
        using var response = await this.RateAsync(Short(surfacingId), "meh");

        await this.corpus.Received(1).RecordAsync(
            Arg.Is<Observation>(o => o.Source == "surfacing-feedback"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostFeedback_Should_Combine_A_Note_With_The_Verdict()
    {
        using var response = await this.RateAsync(Short(surfacingId), "good", "more like this");

        await this.queue.Received(1).RecordFeedbackAsync(
            surfacingId, "good: more like this", Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostFeedback_Should_Return_NotFound_For_An_Unknown_Surfacing()
    {
        using var response = await this.RateAsync("deadbeef", "good");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostFeedback_Should_Not_Deliver_An_Unknown_Surfacing()
    {
        using var response = await this.RateAsync("deadbeef", "good");

        await this.queue.DidNotReceiveWithAnyArgs().DeliverAsync(default, default, default);
    }

    private async Task<HttpResponseMessage> RateAsync(
        string prefix,
        string verdict,
        string? note = null)
    {
        await using var factory = this.CreateFactory();
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(
            $"/surfacings/{prefix}/feedback", new { verdict, note }, CancellationToken.None);
    }

    private static Surfacing Worth(string title)
    {
        return new Surfacing(surfacingId, "interest-scout", title, "the body", 0.72, at);
    }

    private static string Short(Guid id)
    {
        return id.ToString("N")[..8];
    }

    private static async IAsyncEnumerable<Surfacing> SurfacingsAsync(params Surfacing[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISurfacingQueue>();
                services.RemoveAll<IObservationCorpus>();
                services.AddSingleton(this.queue);
                services.AddSingleton(this.corpus);
            }));
    }
}
