using Dami.Contracts.Models;

namespace Dami.Core.Reliability;

/// <summary>Transcribes half a second of silence: the speech sidecar has to load and run its model.</summary>
public sealed class SpeechToTextProbe : ISidecarProbe
{
    private const int SAMPLE_RATE = 16_000;

    private readonly ITranscriptionClient client;

    /// <summary>Creates the probe.</summary>
    public SpeechToTextProbe(ITranscriptionClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    /// <inheritdoc />
    public string Name => "speech-to-text";

    /// <inheritdoc />
    public Task ProbeAsync(CancellationToken cancellationToken) =>
        this.client.TranscribeAsync(Silence(SAMPLE_RATE / 2), "probe.wav", cancellationToken);

    /// <summary>A 16-bit mono PCM WAV of <paramref name="samples"/> zero samples.</summary>
    private static byte[] Silence(int samples)
    {
        var data = samples * 2;
        using var stream = new MemoryStream(44 + data);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SAMPLE_RATE);
        writer.Write(SAMPLE_RATE * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data);
        writer.Write(new byte[data]);
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>Speaks one word: the voice sidecar has to render audio.</summary>
public sealed class TextToSpeechProbe : ISidecarProbe
{
    private readonly ISpeechClient client;

    /// <summary>Creates the probe.</summary>
    public TextToSpeechProbe(ISpeechClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    /// <inheritdoc />
    public string Name => "text-to-speech";

    /// <inheritdoc />
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var audio = await this.client.SpeakAsync("ok", cancellationToken).ConfigureAwait(false);
        if (audio.Length == 0)
        {
            throw new InvalidOperationException("it returned no audio");
        }
    }
}

/// <summary>Embeds one word: the embedding sidecar has to return a vector.</summary>
public sealed class EmbeddingProbe : ISidecarProbe
{
    private readonly IEmbeddingClient client;

    /// <summary>Creates the probe.</summary>
    public EmbeddingProbe(IEmbeddingClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    /// <inheritdoc />
    public string Name => "embeddings";

    /// <inheritdoc />
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var vectors = await this.client.EmbedAsync(["probe"], cancellationToken).ConfigureAwait(false);
        if (vectors.Count == 0 || vectors[0].Length == 0)
        {
            throw new InvalidOperationException("it returned no vector");
        }
    }
}
