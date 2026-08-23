using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>The local chat adapter's trust boundary.</summary>
public sealed class OllamaChatClientTests
{
    [Fact]
    public void Constructor_Should_Reject_A_NonLoopback_Endpoint()
    {
        using var httpClient = new HttpClient();
        var options = Options.Create(new OllamaOptions
        {
            BaseUrl = "https://inference.example.com",
        });

        Assert.Throws<ArgumentException>("ollamaOptions", () => new OllamaChatClient(
            httpClient,
            options,
            NullLogger<OllamaChatClient>.Instance));
    }
}
