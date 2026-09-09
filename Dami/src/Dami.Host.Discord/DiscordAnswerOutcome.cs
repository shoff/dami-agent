namespace Dami.Host.Discord;

/// <summary>What one Discord turn came to: the answer that reached the channel, or why none did.</summary>
/// <remarks>
/// <see cref="DiscordAnswerer"/> explains every failure in the conversation itself, which
/// is right for a live message and wrong on its own for a scheduled job: four refused
/// portraits on 2026-09-08 were each recorded as <c>Succeeded</c> because nothing threw.
/// The outcome lets the scheduled path record the truth without the live path throwing
/// at the gateway loop.
/// </remarks>
public sealed record DiscordAnswerOutcome(string? Answer, string? Failure)
{
    /// <summary>Whether the frontier's answer reached Discord.</summary>
    public bool IsAnswered => this.Failure is null;

    /// <summary>The frontier answered and Discord carried it.</summary>
    public static DiscordAnswerOutcome Answered(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new DiscordAnswerOutcome(answer, null);
    }

    /// <summary>Nothing was answered; the reason has been said in the conversation.</summary>
    public static DiscordAnswerOutcome Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DiscordAnswerOutcome(null, reason);
    }
}
