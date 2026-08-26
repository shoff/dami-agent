using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class SayCommandsTests
{
    [Fact]
    public async Task SayAsync_Should_Write_The_Returned_Audio_To_The_Requested_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dami-say-test-{Guid.NewGuid():N}.wav");
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/speak", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { traceId = Guid.NewGuid(), audioBase64 = Convert.ToBase64String([82, 73, 70, 70]), voice = "v", succeeded = true }),
            });
        }));

        var (exitCode, output) = await CaptureAsync(() => new SayCommands(new DamiApiClient(http)).SayAsync("hello", path, CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Equal([82, 73, 70, 70], await File.ReadAllBytesAsync(path));
        Assert.Contains("[v · 4 bytes", output, StringComparison.Ordinal);
        File.Delete(path);
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

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
