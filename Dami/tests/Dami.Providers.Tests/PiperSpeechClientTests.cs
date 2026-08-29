using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class PiperSpeechClientTests
{
    [Fact]
    public async Task SpeakAsync_Should_Post_Text_And_Voice_To_Loopback_And_Return_The_Wav()
    {
        JsonElement sent = default;
        Uri? target = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            target = request.RequestUri;
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>();
            sent = body!.RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([82, 73, 70, 70]) };
        }));
        var client = new PiperSpeechClient(http, Options.Create(new PiperOptions()));

        var audio = await client.SpeakAsync("hello", CancellationToken.None);

        Assert.Equal("http://127.0.0.1:8091/speak", target!.AbsoluteUri);
        // The voice is sent explicitly on every request, which is why the sidecar's own
        // DAMI_TTS_VOICE never governs what Dami sounds like — a distinction that cost an
        // attempt to "fix" the voice by editing the systemd unit alone.
        Assert.Equal(("hello", "steve-clean"), (sent.GetProperty("text").GetString(), sent.GetProperty("voice").GetString()));
        Assert.Equal([82, 73, 70, 70], audio);
        Assert.Equal("steve-clean", client.VoiceId);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
