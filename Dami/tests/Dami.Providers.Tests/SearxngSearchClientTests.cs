using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class SearxngSearchClientTests
{
    private sealed class Recording : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"query":"x","results":[
                      {"title":"Senior .NET contractor","url":"https://jobs.example/1","content":"Remote, 3 months","engines":["brave"]},
                      {"title":"no url","content":"skip me"},
                      {"title":"Second","url":"https://jobs.example/2","content":"","engines":["duckduckgo","brave"]}
                    ]}
                    """),
            });
        }
    }

    [Fact]
    public async Task Search_Should_Ask_For_Json_And_Parse_The_Installed_Shape()
    {
        var handler = new Recording();
        var client = new SearxngSearchClient(
            new HttpClient(handler), Options.Create(new SearxngOptions { BaseUrl = "http://127.0.0.1:8888" }),
            NullLogger<SearxngSearchClient>.Instance);

        var hits = await client.SearchAsync("dotnet contract work", 5, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:8888/search?format=json&language=en&q=dotnet%20contract%20work", handler.Requested!.AbsoluteUri);
        Assert.Equal(2, hits.Count);
        Assert.Equal(("Senior .NET contractor", "https://jobs.example/1", "Remote, 3 months", "brave"), (hits[0].Title, hits[0].Url.AbsoluteUri, hits[0].Snippet, hits[0].Engine));
        Assert.Equal("duckduckgo,brave", hits[1].Engine);
    }

    [Fact]
    public async Task Search_Should_Honour_The_Limit()
    {
        var client = new SearxngSearchClient(
            new HttpClient(new Recording()), Options.Create(new SearxngOptions()), NullLogger<SearxngSearchClient>.Instance);

        Assert.Single(await client.SearchAsync("x", 1, CancellationToken.None));
    }
}
