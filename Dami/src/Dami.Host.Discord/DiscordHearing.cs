using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Turns a voice note into the words of the message, locally.</summary>
/// <remarks>
/// The audio goes to the loopback speech-to-text sidecar and nowhere else
/// (<see cref="ITranscriptionClient"/>, L3). The transcript then is Steve's message: it is
/// what the frontier answers, what the corpus records, and what a lesson may quote. Voice
/// notes were the most-used capture habit in the 2026-09-29 survey of personal agents.
/// </remarks>
public sealed class DiscordHearing
{
    /// <summary>Past this, a clip is longer than a voice note; a few minutes of Opus is well under it.</summary>
    private const int MAX_BYTES = 25 * 1024 * 1024;

    private readonly ITranscriptionClient transcription;
    private readonly IDiscordRest rest;
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordHearing> logger;

    /// <summary>Creates the listener.</summary>
    public DiscordHearing(
        ITranscriptionClient transcription,
        IDiscordRest rest,
        DiscordOptions options,
        ILogger<DiscordHearing> logger)
    {
        ArgumentNullException.ThrowIfNull(transcription);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this.transcription = transcription;
        this.rest = rest;
        this.options = options;
        this.logger = logger;
    }

    /// <summary>
    /// The message with its first voice note transcribed into the text, after anything
    /// typed; the same message when nothing is audio.
    /// </summary>
    public async Task<InboundMessage> HearAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var clip = message.Attachments.FirstOrDefault(attachment => attachment.IsAudio);
        if (clip is null)
        {
            return message;
        }

        var heard = await this.TranscribeAsync(clip, cancellationToken).ConfigureAwait(false);
        var typed = message.Text.Trim();
        return message with { Text = typed.Length == 0 ? heard : typed + "\n" + heard };
    }

    private async Task<string> TranscribeAsync(InboundAttachment clip, CancellationToken cancellationToken)
    {
        if (clip.SizeBytes > MAX_BYTES)
        {
            return $"(Steve sent a voice note too long to transcribe: {clip.SizeBytes} bytes.)";
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(this.options.VisionTimeout * 4);
            var audio = await this.rest.DownloadAsync(clip.Url, budget.Token).ConfigureAwait(false);
            var text = await this.transcription.TranscribeAsync(audio.ToArray(), clip.FileName, budget.Token)
                .ConfigureAwait(false);
            this.logger.LogInformation("Transcribed a {Bytes}-byte voice note locally", clip.SizeBytes);
            return text.Trim();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(exception, "Could not transcribe Discord voice note {File}", clip.FileName);
            return "(Steve sent a voice note that could not be transcribed; say so and ask him to type it.)";
        }
    }
}
