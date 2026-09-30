using System.Globalization;
using System.Text;
using Dami.Contracts.Privacy;
using Dami.Core.Life;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>"ask …": answered by the local model from Steve's own records, with the sources, in his DM.</summary>
/// <remarks>
/// The records include receipts, meals, INR and forwarded mail, so neither the question's records
/// nor the answer go to a frontier model; the reply is ProfileDerived — his DM only (ADR-0025).
/// </remarks>
public sealed class DiscordAsk : IDiscordCommand
{
    private const string PREFIX = "ask ";
    private const int DISCORD_LIMIT = 2000;
    private const int SOURCE_CHARS = 120;

    private readonly LifeAnswerer answerer;
    private readonly IEgressChannel channel;
    private readonly ILogger<DiscordAsk> logger;

    /// <summary>Creates the command.</summary>
    public DiscordAsk(LifeAnswerer answerer, IEgressChannel channel, ILogger<DiscordAsk> logger)
    {
        ArgumentNullException.ThrowIfNull(answerer);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);
        (this.answerer, this.channel, this.logger) = (answerer, channel, logger);
    }

    /// <inheritdoc />
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Text.Trim().TrimStart('!', '/');
        if (!text.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) || text[PREFIX.Length..].Trim().Length == 0)
        {
            return false;
        }

        var answer = await this.answerer.AnswerAsync(text[PREFIX.Length..].Trim(), cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Asked locally: {Sources} source(s)", answer.Sources.Count);
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, Render(answer), ContentProvenance.ProfileDerived, Guid.NewGuid()), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static string Render(LifeAnswer answer)
    {
        if (answer.Answer is null)
        {
            return "I found nothing in your records about that.";
        }

        var text = new StringBuilder(answer.Answer).Append("\n\nSources:");
        for (var index = 0; index < answer.Sources.Count; index++)
        {
            var source = answer.Sources[index];
            var body = source.Text.ReplaceLineEndings(" ");
            var line = string.Create(CultureInfo.InvariantCulture,
                $"\n[{index + 1}] {source.At:yyyy-MM-dd} {source.Kind}: {(body.Length <= SOURCE_CHARS ? body : body[..SOURCE_CHARS] + "…")}");
            if (text.Length + line.Length > DISCORD_LIMIT)
            {
                break;
            }

            text.Append(line);
        }

        return text.ToString();
    }
}
