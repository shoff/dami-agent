using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class ChatCommandsTests
{
    [Fact]
    public async Task FrontierTurnAsync_Should_Ask_For_Augmentation_Only_When_Told()
    {
        var sent = new List<JsonElement>();
        using var http = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal("/turns", request.RequestUri!.AbsolutePath);
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
            sent.Add(body!.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { answer = "ok", traceId = Guid.NewGuid() }),
            };
        }));
        var commands = new ChatCommands(new DamiApiClient(http));

        var (plain, _) = await CaptureAsync(() => commands.FrontierTurnAsync("hello", CancellationToken.None));
        var (augmented, output) = await CaptureAsync(() => commands.FrontierTurnAsync("hello", augmented: true, CancellationToken.None));

        Assert.Equal((0, 0), (plain, augmented));
        Assert.False(sent[0].GetProperty("augmented").GetBoolean());
        Assert.True(sent[1].GetProperty("augmented").GetBoolean());
        Assert.True(sent[1].GetProperty("frontier").GetBoolean());
        Assert.Contains("disclosure gate", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            return (await run(), writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
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
