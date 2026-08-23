using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>Streaming: fragments in order, thinking excluded.</summary>
public sealed class OllamaChatClientTests
{
    private const string STREAM = """
        {"thinking":"pondering...","done":false}
        {"response":"Hello","done":false}
        {"response":" world","done":false}
        {"response":"","done":true}
        """;

    [Fact]
    public async Task StreamAsync_Should_Yield_Answer_Fragments_In_Order()
    {
        var client = CreateClient();

        var fragments = new List<string>();
        await foreach (var fragment in client.StreamAsync("hi", CancellationToken.None))
        {
            fragments.Add(fragment);
        }

        Assert.Equal(["Hello", " world"], fragments);
    }

    [Fact]
    public async Task StreamAsync_Should_Not_Yield_Thinking_Content()
    {
        var client = CreateClient();

        await foreach (var fragment in client.StreamAsync("hi", CancellationToken.None))
        {
            Assert.DoesNotContain("pondering", fragment);
        }
    }

    private static OllamaChatClient CreateClient()
    {
        var handler = new StreamHandler(STREAM);
        return new OllamaChatClient(
            new HttpClient(handler),
            Options.Create(new OllamaOptions()),
            NullLogger<OllamaChatClient>.Instance);
    }

    private sealed class StreamHandler : HttpMessageHandler
    {
        private readonly string body;

        public StreamHandler(string body)
        {
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.body),
            });
        }
    }
}
