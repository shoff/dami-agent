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
