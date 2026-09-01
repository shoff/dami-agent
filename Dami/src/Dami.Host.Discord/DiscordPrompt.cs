using System.Text;
using Dami.Contracts.Privacy;

namespace Dami.Host.Discord;

/// <summary>Composes what the frontier is asked, from words, pictures and history. Pure.</summary>
/// <remarks>
/// Image captions arrive here already produced by the local vision model and are folded
/// in as ordinary context. That is the shape of ADR-0026 in one place: the local models
/// describe the world, and something better does the thinking about it.
/// </remarks>
public static class DiscordPrompt
{
    /// <summary>The question, with any captions attached to it.</summary>
    public static string Question(InboundMessage message, IReadOnlyList<string> captions)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(captions);

        var text = message.Text.Trim();
        if (captions.Count == 0)
        {
            return text;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("Steve sent image(s). The local vision model describes them as:");
        foreach (var caption in captions)
        {
            prompt.Append("- ").AppendLine(caption.Trim());
        }

        prompt.AppendLine();

        // A photo with no words is still a question. Saying so beats sending an empty
        // line and letting the model guess what was wanted.
        prompt.Append(text.Length > 0
            ? text
            : "He sent this without a message. Say what it is and anything worth noticing.");
        return prompt.ToString();
    }

    /// <summary>Prior exchanges as gate-able lines, oldest first.</summary>
    public static IReadOnlyList<string> Exchanges(
        IReadOnlyList<(string Message, string Response)> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var lines = new List<string>(turns.Count * 2);
        foreach (var (message, response) in turns)
        {
            lines.Add("Earlier — Steve: " + message.Trim());
            lines.Add("Earlier — Dami: " + response.Trim());
        }

        return lines;
    }
}
