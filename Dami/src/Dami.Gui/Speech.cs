using System.Diagnostics;

namespace Dami.Gui;

/// <summary>Plays a rendered WAV through whatever the desktop has.</summary>
/// <remarks>
/// Shells out to <c>paplay</c> then <c>aplay</c>, the same order the CLI's <c>dami say</c>
/// uses, rather than taking an audio dependency for one format on one host. Every failure
/// is swallowed to a reason string: speech is a nicety, and a missing player must never
/// take down a turn that already succeeded.
/// </remarks>
public static class Speech
{
    private static readonly string[] players = ["paplay", "aplay"];

    /// <summary>Writes the audio to a temporary file and plays it. Returns why, if it did not.</summary>
    public static async Task<string?> PlayAsync(byte[] audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Length == 0)
        {
            return "the runtime returned no audio";
        }

        var path = Path.Combine(Path.GetTempPath(), $"dami-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(path, audio, cancellationToken).ConfigureAwait(false);
            return await RunAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            return exception.Message;
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static async Task<string?> RunAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var player in players)
        {
            try
            {
                using var process = Process.Start(
                    new ProcessStartInfo(player, path) { UseShellExecute = false });
                if (process is null)
                {
                    continue;
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return process.ExitCode == 0 ? null : $"{player} exited {process.ExitCode}";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not installed; try the next one.
            }
        }

        return "no audio player found (paplay, aplay)";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temp file that outlives the turn is not worth failing over.
        }
    }
}
