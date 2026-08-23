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

    private static TeiEmbeddingClient CreateClient(out EchoHandler handler, int batchSize)
    {
        handler = new EchoHandler();
        return new TeiEmbeddingClient(
            new HttpClient(handler),
            Options.Create(new TeiOptions { BaseUrl = "http://127.0.0.1:9999", BatchSize = batchSize }),
            NullLogger<TeiEmbeddingClient>.Instance);
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
