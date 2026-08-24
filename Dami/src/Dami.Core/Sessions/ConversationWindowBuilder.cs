using Dami.Contracts.Sessions;
using Microsoft.Extensions.Options;

namespace Dami.Core.Sessions;

/// <summary>Builds recent conversation context without querying durable memory.</summary>
public sealed class ConversationWindowBuilder : IConversationWindowBuilder
{
    private const int CHARS_PER_TOKEN = 4;
    private const int EXCHANGE_OVERHEAD_TOKENS = 12;

    private readonly int maxConversationTokens;
    private readonly int recentTurnLimit;
    private readonly IConversationTurnStore turnStore;

    /// <summary>Creates the builder.</summary>
    public ConversationWindowBuilder(
        IConversationTurnStore turnStore,
        IOptions<SessionContextOptions> options)
    {
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Value.RecentTurnLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Value.MaxConversationTokens);
        this.turnStore = turnStore;
        this.recentTurnLimit = options.Value.RecentTurnLimit;
        this.maxConversationTokens = options.Value.MaxConversationTokens;
    }

    /// <summary>Builds the configured recent window.</summary>
    public async Task<ConversationWindow> BuildAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var turns = new List<ConversationTurn>(this.recentTurnLimit);
        var estimatedTokens = 0;
        await foreach (var turn in this.turnStore.RecentCompletedTurnsAsync(
            sessionId, this.recentTurnLimit, cancellationToken).ConfigureAwait(false))
        {
            turns.Add(turn);
            estimatedTokens += Cost(turn);
        }

        if (estimatedTokens <= this.maxConversationTokens)
        {
            return new ConversationWindow(turns, estimatedTokens);
        }

        var firstKept = turns.Count;
        estimatedTokens = 0;
        for (var index = turns.Count - 1; index >= 0; index--)
        {
            var cost = Cost(turns[index]);
            if (estimatedTokens + cost > this.maxConversationTokens)
            {
                break;
            }

            estimatedTokens += cost;
            firstKept = index;
        }

        turns.RemoveRange(0, firstKept);
        return new ConversationWindow(turns, estimatedTokens);
    }

    private static int Cost(ConversationTurn turn)
    {
        var characters = (long)turn.Request.Message.Length + turn.Response!.Length;
        return checked((int)((characters + CHARS_PER_TOKEN - 1) / CHARS_PER_TOKEN)
            + EXCHANGE_OVERHEAD_TOKENS);
    }
}
