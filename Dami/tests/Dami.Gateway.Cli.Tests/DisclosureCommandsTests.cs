using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class DisclosureCommandsTests
{
    [Fact]
    public async Task ListAsync_Should_Print_Each_Decision_With_Its_Correction()
    {
        var id = Guid.Parse("abcdef12-0000-4000-8000-000000000001");
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/disclosures", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.OK, new[]
            {
                new { decisionId = id, disclosure = "Pass", original = "Steve's surgeon is Dr Harrison", correction = new { corrected = "Withhold" } },
            }));
        }));

        var (exitCode, output) = await CaptureAsync(() => new DisclosureCommands(new DamiApiClient(http)).ListAsync(CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("abcdef12  Pass", output, StringComparison.Ordinal);
        Assert.Contains("→ corrected to Withhold", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectAsync_Should_Post_The_Correction_And_Report_The_Outcome()
    {
        JsonElement sent = default;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal("/disclosures/abcdef12/correct", request.RequestUri!.AbsolutePath);
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
            sent = body!.RootElement.Clone();
            return Json(HttpStatusCode.OK, new { corrected = Guid.NewGuid() });
        }));

        var (exitCode, output) = await CaptureAsync(() => new DisclosureCommands(new DamiApiClient(http))
            .CorrectAsync("abcdef12", "withhold", "doctors' names never leave", CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Equal("withhold", sent.GetProperty("disclosure").GetString());
        Assert.Equal("doctors' names never leave", sent.GetProperty("note").GetString());
        Assert.Equal(BoardActor.FromEnvironment().ActorId, sent.GetProperty("correctedBy").GetString());
        Assert.Contains("corrected to withhold", output, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var original = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            return (await run(), writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
