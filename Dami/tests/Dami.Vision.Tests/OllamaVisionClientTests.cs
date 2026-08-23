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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.LastUri = request.RequestUri;
            this.LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"  a scale model on a workbench \n"}"""),
            };
        }
    }
}
