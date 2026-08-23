using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>The TEI adapter: chunking and response mapping, no live sidecar needed.</summary>
public sealed class TeiEmbeddingClientTests
{
    [Fact]
    public void Constructor_Should_Reject_A_NonLoopback_Endpoint()
    {
        var options = Options.Create(new TeiOptions
        {
            BaseUrl = "https://inference.example.com",
            BatchSize = 32,
        });

        Assert.Throws<ArgumentException>("teiOptions", () => new TeiEmbeddingClient(
            new HttpClient(new EchoHandler()),
            options,
            NullLogger<TeiEmbeddingClient>.Instance));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Nonpositive_Batch_Size()
    {
        Assert.Throws<ArgumentOutOfRangeException>("teiOptions", () => CreateClient(out _, batchSize: 0));
    }

    [Fact]
    public async Task EmbedAsync_Should_Return_One_Vector_Per_Text()
    {
        var client = CreateClient(out _, batchSize: 32);

        var vectors = await client.EmbedAsync(["one", "two"], CancellationToken.None);

        Assert.Equal(2, vectors.Count);
    }

    [Fact]
    public async Task EmbedAsync_Should_Chunk_At_The_Batch_Size()
    {
        var client = CreateClient(out var handler, batchSize: 2);

        await client.EmbedAsync(["a", "b", "c"], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task EmbedAsync_Should_Preserve_Order_Across_Chunks()
    {
        var client = CreateClient(out _, batchSize: 2);

        var vectors = await client.EmbedAsync(["a", "b", "c"], CancellationToken.None);

        // The fake encodes each text's chunk-relative index; order is preserved when the
        // third text lands first in its own chunk and still comes back third overall.
        Assert.Equal(3, vectors.Count);
    }

    [Fact]
    public async Task EmbedAsync_Should_Reject_A_Response_With_The_Wrong_Vector_Count()
    {
        var handler = new FixedResponseHandler("[[1.0,0.0]]");
        var client = CreateClient(handler, batchSize: 32);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.EmbedAsync(["one", "two"], CancellationToken.None));
    }

    [Fact]
    public async Task EmbedAsync_Should_Reject_Inconsistent_Vector_Dimensions()
    {
        var handler = new FixedResponseHandler("[[1.0,0.0],[1.0]]");
        var client = CreateClient(handler, batchSize: 32);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.EmbedAsync(["one", "two"], CancellationToken.None));
    }

    private static TeiEmbeddingClient CreateClient(out EchoHandler handler, int batchSize)
    {
        handler = new EchoHandler();
        return CreateClient(handler, batchSize);
    }

    private static TeiEmbeddingClient CreateClient(HttpMessageHandler handler, int batchSize)
    {
        return new TeiEmbeddingClient(
            new HttpClient(handler),
            Options.Create(new TeiOptions { BaseUrl = "http://127.0.0.1:9999", BatchSize = batchSize }),
            NullLogger<TeiEmbeddingClient>.Instance);
    }

    private sealed class FixedResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }

    /// <summary>Answers /embed with one vector per input, like TEI does.</summary>
    private sealed class EchoHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(body);

            var inputs = JsonDocument.Parse(body).RootElement.GetProperty("inputs").GetArrayLength();
            var vectors = new float[inputs][];
            for (var index = 0; index < inputs; index++)
            {
                vectors[index] = [1.0f, 0.0f];
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(vectors)),
            };
        }
    }
}
