using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Vision.Tests;

/// <summary>The adapter's request shape and response mapping, no live sidecar needed.</summary>
public sealed class OllamaVisionClientTests
{
    [Fact]
    public async Task DescribeAsync_Should_Send_The_Image_As_Base64()
    {
        var client = CreateClient(out var handler);
        byte[] image = [1, 2, 3, 4];

        await client.DescribeAsync(image, "caption this", CancellationToken.None);

        var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(
            Convert.ToBase64String(image),
            sent.RootElement.GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task DescribeAsync_Should_Ask_Ollama_To_Unload_The_Model_Afterwards()
    {
        // 2026-09-05: the vision model left resident beside qwen3 put one of them half on
        // the CPU, the LLM guard restarted the sidecar under a live turn, and a gym photo
        // failed. keep_alive 0 hands the card back the moment the caption is done.
        var client = CreateClient(out var handler);

        await client.DescribeAsync(new byte[] { 1 }, "caption this", CancellationToken.None);

        var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(0, sent.RootElement.GetProperty("keep_alive").GetInt32());
    }

    [Fact]
    public async Task DescribeAsync_Should_Unload_The_Text_Model_Before_Loading_The_Vision_Model()
    {
        // 2026-09-05 21:50: qwen3 pinned "100% CPU, Forever" after the vision model took the
        // card; every local call crawled and the image decode returned 400.
        var client = CreateClient(out var handler);

        await client.DescribeAsync(new byte[] { 1 }, "caption this", CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
        var evict = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("qwen3:8b", evict.GetProperty("model").GetString());
        Assert.Equal(0, evict.GetProperty("keep_alive").GetInt32());
        Assert.Equal("qwen2.5vl:7b", JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task DescribeAsync_Should_Return_The_Trimmed_Response()
    {
        var client = CreateClient(out _);

        var description = await client.DescribeAsync(new byte[4], "caption", CancellationToken.None);

        Assert.Equal("a scale model on a workbench", description);
    }

    [Fact]
    public async Task DescribeAsync_Should_Target_Loopback()
    {
        var client = CreateClient(out var handler);

        await client.DescribeAsync(new byte[4], "caption", CancellationToken.None);

        Assert.Equal("127.0.0.1", handler.LastUri!.Host);
    }

    private static OllamaVisionClient CreateClient(out RecordingHandler handler)
    {
        handler = new RecordingHandler();
        return new OllamaVisionClient(
            new HttpClient(handler),
            Options.Create(new OllamaVisionOptions()),
            NullLogger<OllamaVisionClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public Uri? LastUri { get; private set; }

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.LastUri = request.RequestUri;
            this.LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            this.Bodies.Add(this.LastBody);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"  a scale model on a workbench \n"}"""),
            };
        }
    }
}
