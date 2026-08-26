using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class DomainCommandsTests
{
    [Fact]
    public async Task ShowAsync_Should_List_Domains_Then_A_Timeline()
    {
        var id = Guid.Parse("abcdef12-0000-4000-8000-000000000001");
        using var http = new HttpClient(new StubHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/domains"
                ? Json(new[] { new { domain = "network", facts = 7 } })
                : Json(new[] { new { factId = id, asOf = "2026-08-25", category = "gateway", description = "Default gateway is 192.168.4.1" } }))));
        var commands = new DomainCommands(new DamiApiClient(http));

        var (listExit, list) = await CaptureAsync(() => commands.ShowAsync(null, CancellationToken.None));
        var (timelineExit, timeline) = await CaptureAsync(() => commands.ShowAsync("network", CancellationToken.None));

        Assert.Equal((0, 0), (listExit, timelineExit));
        Assert.Contains("network", list, StringComparison.Ordinal);
        Assert.Contains("7 facts", list, StringComparison.Ordinal);
        Assert.Contains("abcdef12  2026-08-25  [gateway]  Default gateway is 192.168.4.1", timeline, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
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
