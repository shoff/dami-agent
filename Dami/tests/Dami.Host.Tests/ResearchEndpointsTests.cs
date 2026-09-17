using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Contracts.Research;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class ResearchEndpointsTests
{
    [Theory]
    [InlineData("list", HttpStatusCode.OK)]
    [InlineData("detail", HttpStatusCode.OK)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("invalid", HttpStatusCode.BadRequest)]
    public async Task Research_Should_Expose_Saved_Artifacts_And_Validate_History_Requests(string route, HttpStatusCode expected)
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Find open data", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z")) { Status = ResearchRunStatus.Completed,
            Findings = [new ResearchFinding(new ResearchPage(new Uri("https://public.example/"), 200, "Source", "Source body"),
                0, [new Uri("https://public.example/")])] };
        var store = Substitute.For<IResearchRunStore>();
        store.ListAsync(25, Arg.Any<CancellationToken>()).Returns(new[] { run.Summarize() });
        store.GetAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var path = route switch { "list" => "/research/runs?limit=25", "detail" => $"/research/runs/{run.RunId}",
            "invalid" => "/research/runs?limit=0", _ => $"/research/runs/{Guid.NewGuid()}" };
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("Find open data", text, StringComparison.Ordinal);
            Assert.Equal(route == "detail", text.Contains("Source body", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(text);
            Assert.Equal(route == "list" ? JsonValueKind.Array : JsonValueKind.Object, json.RootElement.ValueKind);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IResearchRunStore store) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IResearchRunStore>();
            services.AddSingleton(store);
        }));

    [Fact]
    public async Task Streaming_Turn_Should_Archive_The_Research_Answer_Under_The_Response_Trace()
    {
        var store = Substitute.For<IResearchRunStore>();
        store.FindByTraceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            new ResearchRun(Guid.NewGuid(), call.Arg<Guid>(), "Research question", new Uri("https://public.example/"),
                DateTimeOffset.Parse("2026-09-11T12:00:00Z")) { Status = ResearchRunStatus.Completed });
        var frontier = Substitute.For<IFrontierChat>();
        frontier.StreamAsync(Arg.Any<FrontierPrompt>(), Arg.Any<IReadOnlyList<FrontierImage>>(),
            Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>()).Returns(AnswerAsync());
        await using var factory = CreateFactory(store).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFrontierChat>();
            services.AddSingleton(frontier);
        }));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/turns/stream", new { message = "Research data", frontier = true });
        response.EnsureSuccessStatusCode();
        var trace = Guid.Parse(response.Headers.GetValues("X-Dami-Trace").Single());
        Assert.Contains("Source-backed answer", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await store.Received(1).SaveAsync(Arg.Is<ResearchRun>(run => run.TraceId == trace
            && run.Answer == "Source-backed answer" && run.AnswerStatus == ResearchAnswerStatus.Complete), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<string> AnswerAsync()
    {
        yield return "Source-backed answer";
        await Task.CompletedTask;
    }
}
