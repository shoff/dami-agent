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

    [Fact]
    public async Task StreamAsync_Should_Pin_The_Model_Resident()
    {
        var handler = new StreamHandler(STREAM);
        var client = CreateClient(handler);

        await foreach (var _ in client.StreamAsync("hi", CancellationToken.None))
        {
            // drain
        }

        Assert.Contains("\"keep_alive\":-1", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_Should_Pin_The_Model_Resident()
    {
        var handler = new StreamHandler("""{"response":"hi","done":true}""");
        var client = CreateClient(handler);

        await client.CompleteAsync("hi", CancellationToken.None);

        Assert.Contains("\"keep_alive\":-1", handler.LastBody, StringComparison.Ordinal);
    }

    private static OllamaChatClient CreateClient(StreamHandler? handler = null)
    {
        return new OllamaChatClient(
            new HttpClient(handler ?? new StreamHandler(STREAM)),
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

        /// <summary>The JSON the client last sent — what the sidecar would actually receive.</summary>
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                this.LastBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.body),
            };
        }
    }
}
