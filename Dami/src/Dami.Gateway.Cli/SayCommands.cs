using System.Diagnostics;

namespace Dami.Gateway.Cli;

/// <summary>`dami say &lt;text&gt;` — Dami speaks, locally (L4).</summary>
public sealed class SayCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public SayCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>
    /// Renders the text through the runtime and either writes the WAV to
    /// <paramref name="outputPath"/> or plays it with the first player found.
    /// </summary>
    public Task<int> SayAsync(string text, string? outputPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync("/speak", new { text }, cancellationToken).ConfigureAwait(false);
            var root = reply!.RootElement;
            if (!root.GetProperty("succeeded").GetBoolean())
            {
                await Console.Error.WriteLineAsync(
                    $"the speech sidecar failed: dami trace {root.GetProperty("traceId").GetGuid().ToString("N")[..8]}")
                    .ConfigureAwait(false);
                return 1;
            }

            var audio = Convert.FromBase64String(root.GetProperty("audioBase64").GetString()!);
            var path = outputPath ?? Path.Combine(Path.GetTempPath(), $"dami-say-{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(path, audio, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[{root.GetProperty("voice").GetString()} · {audio.Length} bytes · trace {root.GetProperty("traceId").GetGuid().ToString("N")[..8]}]");
            return outputPath is null ? await PlayAsync(path, cancellationToken).ConfigureAwait(false) : 0;
        });
    }

    private static async Task<int> PlayAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var player in new[] { "paplay", "aplay" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(player, path) { UseShellExecute = false });
                if (process is null)
                {
                    continue;
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not installed; try the next player.
            }
        }

        Console.WriteLine($"no player found (paplay/aplay); audio at {path}");
        return 0;
    }
}
