using Dami.Contracts.Privacy;

namespace Dami.Host.Discord;

/// <summary>Splits a Discord message into what may be asked and what must be gated. Pure.</summary>
/// <remarks>
/// The split is the privacy boundary, not a formatting choice. <see cref="Question"/> is
/// Steve's own words, which he chose to send and which the augmented turn appends to the
/// frontier prompt ungated. <see cref="LocalContext"/> is everything this host derived —
/// prior exchanges and image captions — and it goes through
/// <c>LocalDisclosureGate</c> with retrieved memory.
///
/// An earlier version folded captions into the question, which egressed them ungated: an
/// image is LocalOnly under D-012 and the local vision model is told to transcribe any
/// text it sees, so a photographed lab result or statement left the host verbatim. The
/// caption is derived from local-only data and is therefore gate-able context, never part
/// of the ask.
/// </remarks>
public static class DiscordPrompt
{
    /// <summary>What Steve asked, in his own words.</summary>
    public static string Question(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var text = message.Text.Trim();
        if (text.Length > 0)
        {
            return text;
        }

        // A photo with no words is still a question; an empty prompt would be rejected
        // before it reached a model.
        return message.Attachments.Any(attachment => attachment.IsImage)
            ? "Steve sent an image without a message. Say what it is and anything worth noticing."
            : string.Empty;
    }

    /// <summary>
    /// Everything derived on this host for this turn, oldest first, for the gate to judge.
    /// </summary>
    public static IReadOnlyList<string> LocalContext(
        IReadOnlyList<(string Message, string Response)> turns,
        IReadOnlyList<string> captions)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(captions);

        var lines = new List<string>((turns.Count * 2) + captions.Count);
        foreach (var (message, response) in turns)
        {
            lines.Add("Earlier — Steve: " + message.Trim());
            lines.Add("Earlier — Dami: " + response.Trim());
        }

        foreach (var caption in captions)
        {
            // Labelled so the gate is judging a described image rather than a loose noun.
            lines.Add("Image Steve sent, described locally: " + caption.Trim());
        }

        return lines;
    }
}
