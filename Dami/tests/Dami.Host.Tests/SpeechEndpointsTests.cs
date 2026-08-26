using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class SpeechEndpointsTests
{
    [Fact]
    public async Task Speak_Should_Run_The_Sidecar_As_A_Worker_And_Return_Base64_Audio()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISpeechClient>();
                services.AddSingleton<ISpeechClient>(new StubSpeech());
            }));
        using var client = factory.CreateClient();

        using var spoken = await client.PostAsJsonAsync("/speak", new { text = "hello" }, CancellationToken.None);
        using var body = await spoken.Content.ReadFromJsonAsync<JsonDocument>();
        using var empty = await client.PostAsJsonAsync("/speak", new { text = " " }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, spoken.StatusCode);
        Assert.True(body!.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(body.RootElement.GetProperty("audioBase64").GetString()!)));
        Assert.Equal("test-voice", body.RootElement.GetProperty("voice").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    private sealed class StubSpeech : ISpeechClient
    {
        public string VoiceId => "test-voice";

        public Task<byte[]> SpeakAsync(string text, CancellationToken cancellationToken)
        {
            return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        }
    }
}
