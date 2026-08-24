namespace Dami.Gateway.Cli;

/// <summary>`dami listen &lt;audio-file&gt;` — local speech to text (L3).</summary>
public sealed class ListenCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public ListenCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Transcribes one audio file. The audio never leaves the host.</summary>
    public async Task<int> TranscribeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            await Console.Error.WriteLineAsync($"no such file: {path}").ConfigureAwait(false);
            return 1;
        }

        var audio = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await ApiCall.RunAsync(async () =>
        {
            Console.WriteLine("listening (local model; the audio never leaves the host)...");
            using var reply = await this.api.PostAsync(
                "/transcribe", new { audioBase64 = Convert.ToBase64String(audio) },
                cancellationToken).ConfigureAwait(false);
            var root = reply!.RootElement;
            Console.WriteLine();
            Console.WriteLine(root.GetProperty("text").GetString());
            Console.WriteLine();
            Console.WriteLine(
                $"[{root.GetProperty("model").GetString()} · trace "
                + $"{root.GetProperty("traceId").GetGuid().ToString("N")[..8]}]");
            return root.GetProperty("succeeded").GetBoolean() ? 0 : 1;
        }).ConfigureAwait(false);
    }
}
