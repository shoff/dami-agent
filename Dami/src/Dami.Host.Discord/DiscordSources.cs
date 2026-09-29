using System.Text;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Answers "sources" / "why?" with what the last answer in the conversation drew on.</summary>
/// <remarks>
/// Per-reply memory provenance is what the commercial assistants now show and what Dami's
/// traces make exact (docs/agent-landscape-2026-09.md B1): every local line the turn put
/// before the disclosure gate, and whether it was sent, disguised or withheld and why. The
/// lines are Steve's own memory, so the reply is ProfileDerived — his DM only (ADR-0025).
/// Nothing is recomputed and no model is asked.
/// </remarks>
public sealed class DiscordSources
{
    private const int DISCORD_LIMIT = 2000;
    private const int LINE_CHARS = 160;

    private readonly DiscordLastTurns lastTurns;
    private readonly TurnDisclosures disclosures;
    private readonly IEgressChannel channel;
    private readonly ILogger<DiscordSources> logger;

    /// <summary>Creates the responder.</summary>
    public DiscordSources(
        DiscordLastTurns lastTurns, TurnDisclosures disclosures, IEgressChannel channel, ILogger<DiscordSources> logger)
    {
        ArgumentNullException.ThrowIfNull(lastTurns);
        ArgumentNullException.ThrowIfNull(disclosures);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);
        this.lastTurns = lastTurns;
        this.disclosures = disclosures;
        this.channel = channel;
        this.logger = logger;
    }

    /// <summary>Answers when the message is exactly the question; false otherwise.</summary>
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var asked = message.Text.Trim().TrimStart('!', '/').Trim().TrimEnd('?').ToLowerInvariant();
        if (asked is not ("sources" or "why"))
        {
            return false;
        }

        var trace = this.lastTurns.LastIn(message.ConversationId);
        var decided = trace is { } id ? this.disclosures.For(id) : null;
        var text = decided is null
            ? "There is no recent answer here to explain (the record is kept for the last 64 turns, until a restart)."
            : Render(trace!.Value, decided);
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, text, ContentProvenance.ProfileDerived, trace ?? Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Sources shown for {Trace}", trace);
        return true;
    }

    private static string Render(Guid trace, IReadOnlyList<DisclosedItem> decided)
    {
        var text = new StringBuilder(
            $"The last answer (trace {trace:N}) put {decided.Count} local line(s) before the gate:");
        var shown = 0;
        foreach (var item in decided)
        {
            var line = "\n" + Line(item);
            if (text.Length + line.Length > DISCORD_LIMIT - 60)
            {
                break;
            }

            text.Append(line);
            shown++;
        }

        if (shown < decided.Count)
        {
            text.Append($"\n…and {decided.Count - shown} more; `dami trace {trace:N}` has them all.");
        }

        return text.ToString();
    }

    private static string Line(DisclosedItem item) => item.Disclosure switch
    {
        Disclosure.Pass => "✅ sent: " + Bound(item.Original),
        Disclosure.Disguise => $"🎭 disguised as \"{Bound(item.Sendable)}\": {Bound(item.Original)}",
        _ => $"⛔ withheld ({item.Reason}): {Bound(item.Original)}",
    };

    private static string Bound(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= LINE_CHARS ? flat : flat[..LINE_CHARS] + "…";
    }
}
