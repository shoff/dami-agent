using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>The local reranker adapter's trust boundary.</summary>
public sealed class TeiRerankClientTests
{
    [Fact]
    public void Constructor_Should_Reject_A_NonLoopback_Endpoint()
    {
        using var httpClient = new HttpClient();
        var options = Options.Create(new TeiRerankOptions
        {
            BaseUrl = "https://inference.example.com",
        });

        Assert.Throws<ArgumentException>("rerankOptions", () => new TeiRerankClient(
            httpClient,
            options,
            NullLogger<TeiRerankClient>.Instance));
    }

    [Fact]
    public async Task RankAsync_Should_Reject_An_OutOfRange_Index()
    {
        using var httpClient = new HttpClient(new FixedResponseHandler(
            "[{\"index\":2,\"score\":0.9}]"));
        var client = new TeiRerankClient(
            httpClient,
            Options.Create(new TeiRerankOptions { BaseUrl = "http://127.0.0.1:9999" }),
            NullLogger<TeiRerankClient>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RankAsync("query", ["one", "two"], CancellationToken.None));
    }

    [Fact]
    public async Task RankAsync_Should_Reject_A_Duplicate_Index()
    {
        using var httpClient = new HttpClient(new FixedResponseHandler(
            "[{\"index\":0,\"score\":0.9},{\"index\":0,\"score\":0.8}]"));
        var client = new TeiRerankClient(
            httpClient,
            Options.Create(new TeiRerankOptions { BaseUrl = "http://127.0.0.1:9999" }),
            NullLogger<TeiRerankClient>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RankAsync("query", ["one", "two"], CancellationToken.None));
    }

    [Fact]
    public async Task RankAsync_Should_Batch_At_The_Client_Limit_And_Merge_The_Scores()
    {
        // TEI's max-client-batch-size is 32; forty candidates in one request is a 422.
        var handler = new BatchScoringHandler();
        using var httpClient = new HttpClient(handler);
        var client = new TeiRerankClient(
            httpClient,
            Options.Create(new TeiRerankOptions { BaseUrl = "http://127.0.0.1:9999", MaxBatch = 3 }),
            NullLogger<TeiRerankClient>.Instance);
        var candidates = Enumerable.Range(0, 7).Select(i => $"c{i}").ToList();

        var order = await client.RankAsync("q", candidates, CancellationToken.None);

        Assert.Equal([3, 3, 1], handler.BatchSizes);
        // The handler scores candidate i as i, so the best is the last one overall.
        Assert.Equal([6, 5, 4, 3, 2, 1, 0], order);
    }

    private sealed class BatchScoringHandler : HttpMessageHandler
    {
        public List<int> BatchSizes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var texts = body.RootElement.GetProperty("texts").EnumerateArray().Select(t => t.GetString()!).ToList();
            this.BatchSizes.Add(texts.Count);
            var scored = texts.Select((text, index) => $"{{\"index\":{index},\"score\":{text[1..]}}}");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[" + string.Join(",", scored) + "]") };
        }
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
}
