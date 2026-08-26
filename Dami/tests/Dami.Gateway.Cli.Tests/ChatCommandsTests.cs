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

    [Fact]
    public async Task TurnAsync_Should_Speak_The_Whole_Answer_After_Streaming_It()
    {
        string? spoken = null;
        var wav = Path.Combine(Path.GetTempPath(), $"dami-speak-test-{Guid.NewGuid():N}.wav");
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/speak")
            {
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
                spoken = body!.RootElement.GetProperty("text").GetString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { traceId = Guid.NewGuid(), audioBase64 = Convert.ToBase64String([1, 2]), voice = "v", succeeded = true }),
                };
            }

            var stream = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: Hello\n\ndata:  there.\n\n"),
            };
            stream.Headers.Add("X-Dami-Trace", Guid.NewGuid().ToString("N"));
            return stream;
        }));
        var api = new DamiApiClient(http);
        var say = new SpeakingIntoFile(api, wav);

        var (exitCode, output) = await CaptureAsync(() => new ChatCommands(api).TurnAsync("hi", say, CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Hello there.", output, StringComparison.Ordinal);
        Assert.Equal("Hello there.", spoken);
        File.Delete(wav);
    }

    /// <summary>A SayCommands that writes instead of playing, so the test needs no audio device.</summary>
    private sealed class SpeakingIntoFile(DamiApiClient api, string path) : SayCommands(api)
    {
        public override Task<int> SayAsync(string text, string? outputPath, CancellationToken cancellationToken)
        {
            return base.SayAsync(text, path, cancellationToken);
        }
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
