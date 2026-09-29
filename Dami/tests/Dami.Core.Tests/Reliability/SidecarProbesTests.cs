using Dami.Contracts.Models;
using Dami.Core.Reliability;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Reliability;

public sealed class SidecarProbesTests
{
    [Fact]
    public async Task Speech_To_Text_Should_Be_Probed_With_Real_Audio()
    {
        var client = Substitute.For<ITranscriptionClient>();
        byte[]? sent = null;
        client.TranscribeAsync(Arg.Do<byte[]>(audio => sent = audio), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(string.Empty);

        await new SpeechToTextProbe(client).ProbeAsync(CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("RIFF"u8.ToArray(), sent![..4]);
        Assert.True(sent.Length > 1000);
    }

    [Fact]
    public async Task Text_To_Speech_Should_Fail_The_Probe_On_Empty_Audio()
    {
        var client = Substitute.For<ISpeechClient>();
        client.SpeakAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TextToSpeechProbe(client).ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Embeddings_Should_Fail_The_Probe_On_An_Empty_Vector()
    {
        var client = Substitute.For<IEmbeddingClient>();
        client.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([Array.Empty<float>()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EmbeddingProbe(client).ProbeAsync(CancellationToken.None));
    }
}
