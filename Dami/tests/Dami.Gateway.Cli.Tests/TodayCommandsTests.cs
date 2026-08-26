using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Gateway.Cli.Tests;

[Collection("Console")]
public sealed class TodayCommandsTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShowAsync_Should_Read_Four_Sections_And_Keep_Only_What_Matters()
    {
        var boardId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request => Task.FromResult(Respond(request, boardId))));

        var (exitCode, output) = await CaptureAsync(() => new TodayCommands(new DamiApiClient(http), new FakeTimeProvider(now)).ShowAsync(CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("inbox    1 pending", output, StringComparison.Ordinal);
        Assert.Contains("board    1 task(s) in progress · 1 waiting on you", output, StringComparison.Ordinal);
        Assert.Contains("A7 ADR-0001", output, StringComparison.Ordinal);
        Assert.DoesNotContain("E3 UDP", output, StringComparison.Ordinal);
        Assert.Contains("civic    1 meeting(s) this week", output, StringComparison.Ordinal);
        Assert.Contains("Wed 08-26  Finance Committee Meeting", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Far away", output, StringComparison.Ordinal);
        Assert.Contains("network  1 problem(s) as of 2026-08-25", output, StringComparison.Ordinal);
        Assert.Contains("ollama on 127.0.0.1:11434 is not listening", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dami-stt", output, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, Guid boardId)
    {
        return request.RequestUri!.AbsolutePath switch
        {
            "/surfacings" => Json(new[] { new { surfacingId = Guid.NewGuid(), title = "Civic calendar, week of 2026-08-25: 2 meeting(s)", confidence = 0.6 } }),
            "/task-boards" => Json(new[] { new { boardId } }),
            var path when path.StartsWith("/task-boards/", StringComparison.Ordinal) => Json(new
            {
                tasks = new[]
                {
                    Node("Epic", "Open", "epic", [
                        Node("A7 ADR-0001 accept/reject", "Blocked", "- [ ] A7 ADR-0001 accept/reject `[STEVE]`", []),
                        Node("O2 board", "InProgress", "claimed", []),
                        Node("E3 UDP", "Blocked", "- [ ] E3 UDP `[BLOCKED: L-phase]`", []),
                    ]),
                },
            }),
            "/domains/civic" => Json(new[]
            {
                new { asOf = "2026-08-26", category = "meeting", description = "Finance Committee Meeting — https://x/1" },
                new { asOf = "2026-08-26", category = "notice", description = "Family Flicks — https://x/2" },
                new { asOf = "2026-09-20", category = "meeting", description = "Far away — https://x/3" },
            }),
            "/domains/network" => Json(new[]
            {
                new { asOf = "2026-08-25", category = "service", description = "ollama on 127.0.0.1:11434 is not listening" },
                new { asOf = "2026-08-25", category = "service", description = "postgresql on 127.0.0.1:5432 is listening" },
                new { asOf = "2026-08-24", category = "service", description = "dami-stt on 127.0.0.1:8090 is not listening" },
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static object Node(string title, string status, string description, object[] children)
    {
        return new { taskId = Guid.NewGuid(), title, status, description, subTasks = children };
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
